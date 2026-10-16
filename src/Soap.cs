using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace OpenNfceDownload
{
    /// <summary>Os endereços são fixos. O WSDL autenticado determina action e invólucro SOAP.
    /// Não tenta operações por tentativa e erro e nunca desativa a validação TLS.</summary>
    public sealed class SoapContract
    {
        static readonly XNamespace W = "http://schemas.xmlsoap.org/wsdl/";
        static readonly XNamespace X = "http://www.w3.org/2001/XMLSchema";
        static readonly XNamespace B12 = "http://schemas.xmlsoap.org/wsdl/soap12/";
        static readonly XNamespace B11 = "http://schemas.xmlsoap.org/wsdl/soap/";
        public string Action, Operation;
        public bool Soap12;
        public XDocument Definition;
        public XName InputElement;
        public readonly List<XName> Headers = new List<XName>();

        static XName QName(XElement at, string value)
        {
            if (String.IsNullOrEmpty(value)) throw new InvalidDataException("Referência ausente no WSDL.");
            int colon = value.IndexOf(':');
            if (colon < 0) return at.GetDefaultNamespace() + value;
            XNamespace ns = at.GetNamespaceOfPrefix(value.Substring(0, colon));
            if (ns == null) throw new InvalidDataException("Namespace ausente no WSDL.");
            return ns + value.Substring(colon + 1);
        }
        static XElement Named(XDocument doc, XName tag, XName name)
        {
            return doc.Descendants(tag).FirstOrDefault(e => (string)e.Attribute("name") == name.LocalName &&
                (string)e.Parent.Attribute("targetNamespace") == name.NamespaceName);
        }
        public static SoapContract Parse(XDocument doc, string preferred)
        {
            if (doc.Root == null || doc.Root.Name != W + "definitions") throw new InvalidDataException("O servidor não devolveu um WSDL válido.");
            foreach (XNamespace soap in new[] { B12, B11 })
            {
                var candidates = doc.Root.Elements(W + "binding").Where(b => b.Element(soap + "binding") != null)
                    .SelectMany(b => b.Elements(W + "operation")).Where(o => o.Element(soap + "operation") != null).ToList();
                if (candidates.Count == 0) continue;
                var op = candidates.FirstOrDefault(o => String.Equals((string)o.Attribute("name"), preferred, StringComparison.OrdinalIgnoreCase));
                if (op == null && candidates.Count == 1) op = candidates[0];
                if (op == null) throw new InvalidDataException("Operação " + preferred + " não encontrada no WSDL oficial.");
                XElement binding = op.Parent;
                XName ptName = QName(binding, (string)binding.Attribute("type"));
                XElement pt = Named(doc, W + "portType", ptName);
                XElement ptOp = pt == null ? null : pt.Elements(W + "operation").FirstOrDefault(e => (string)e.Attribute("name") == (string)op.Attribute("name"));
                if (ptOp == null || ptOp.Element(W + "input") == null) throw new InvalidDataException("Entrada da operação ausente no WSDL.");
                var contract = new SoapContract { Definition = doc, Operation = (string)op.Attribute("name"),
                    Soap12 = soap == B12, Action = (string)op.Element(soap + "operation").Attribute("soapAction") ?? "" };
                XElement input = ptOp.Element(W + "input");
                XElement message = Named(doc, W + "message", QName(input, (string)input.Attribute("message")));
                var parts = message == null ? new List<XElement>() : message.Elements(W + "part").ToList();
                if (parts.Count != 1 || parts[0].Attribute("element") == null) throw new InvalidDataException("WSDL deve usar uma entrada document/literal.");
                contract.InputElement = QName(parts[0], (string)parts[0].Attribute("element"));
                XElement bindingInput = op.Element(W + "input");
                if (bindingInput != null) foreach (var h in bindingInput.Elements(soap + "header"))
                {
                    XElement hm = Named(doc, W + "message", QName(h, (string)h.Attribute("message")));
                    XElement hp = hm == null ? null : hm.Elements(W + "part").FirstOrDefault(p => (string)p.Attribute("name") == (string)h.Attribute("part"));
                    if (hp == null) throw new InvalidDataException("Cabeçalho WSDL inválido.");
                    contract.Headers.Add(QName(hp, (string)hp.Attribute("element")));
                }
                return contract;
            }
            throw new InvalidDataException("Nenhum binding SOAP encontrado no WSDL oficial.");
        }
        XElement Shape(XElement element)
        {
            XElement shape = element.Element(X + "complexType");
            if (shape != null) return shape;
            string type = (string)element.Attribute("type");
            if (type == null) return null;
            return Named(Definition, X + "complexType", QName(element, type));
        }
        XElement BuildElement(XName name, XElement schemaElement, XElement payload, string uf, bool header, int depth)
        {
            if (depth > 5) throw new InvalidDataException("Estrutura WSDL inesperada.");
            var result = new XElement(name);
            XElement shape = Shape(schemaElement);
            string type = (string)schemaElement.Attribute("type");
            if (type != null && QName(schemaElement, type) == X + "string")
            {
                if (header) throw new InvalidDataException("Cabeçalho WSDL não suportado.");
                result.Value = payload.ToString(SaveOptions.DisableFormatting); return result;
            }
            XElement seq = shape == null ? null : shape.Element(X + "sequence");
            if (seq != null && seq.Element(X + "any") != null)
            {
                if (header) throw new InvalidDataException("Cabeçalho WSDL sem campos reconhecíveis.");
                result.Add(new XElement(payload)); return result;
            }
            var fields = seq == null ? new List<XElement>() : seq.Elements(X + "element").ToList();
            // Elemento XmlNode/anyType sem restrição de conteúdo.
            if (fields.Count == 0 && !header && (type == null || QName(schemaElement, type) == X + "anyType"))
            { result.Add(new XElement(payload)); return result; }
            if (fields.Count == 0) throw new InvalidDataException("Tipo de entrada não suportado no WSDL.");
            foreach (XElement field in fields)
            {
                XName childName;
                XElement declaration = field;
                if (field.Attribute("ref") != null)
                {
                    childName = QName(field, (string)field.Attribute("ref"));
                    declaration = Named(Definition, X + "element", childName);
                    if (declaration == null) throw new InvalidDataException("Schema importado ausente no WSDL: " + childName);
                }
                else
                {
                    XElement schema = field.Ancestors(X + "schema").First();
                    bool qualified = (string)field.Attribute("form") == "qualified" ||
                        (field.Attribute("form") == null && (string)schema.Attribute("elementFormDefault") == "qualified");
                    childName = XName.Get((string)field.Attribute("name"), qualified ? (string)schema.Attribute("targetNamespace") ?? "" : "");
                }
                if (header)
                {
                    if (childName.LocalName == "cUF") result.Add(new XElement(childName, uf));
                    else if (childName.LocalName == "versaoDados") result.Add(new XElement(childName, "1.00"));
                    else throw new InvalidDataException("Campo de cabeçalho desconhecido: " + childName.LocalName);
                }
                else
                {
                    if (fields.Count != 1) throw new InvalidDataException("Entrada WSDL com mais de um parâmetro não suportada.");
                    result.Add(BuildElement(childName, declaration, payload, uf, false, depth + 1));
                }
            }
            return result;
        }
        public byte[] Envelope(XElement payload, string uf)
        {
            XElement input = Named(Definition, X + "element", InputElement);
            if (input == null) throw new InvalidDataException("Schema da entrada não encontrado no WSDL oficial.");
            XNamespace soap = Soap12 ? "http://www.w3.org/2003/05/soap-envelope" : "http://schemas.xmlsoap.org/soap/envelope/";
            var envelope = new XElement(soap + "Envelope", new XAttribute(XNamespace.Xmlns + "s", soap));
            if (Headers.Count > 0)
            {
                var header = new XElement(soap + "Header");
                foreach (XName h in Headers)
                {
                    XElement declaration = Named(Definition, X + "element", h);
                    if (declaration == null) throw new InvalidDataException("Schema do cabeçalho ausente.");
                    header.Add(BuildElement(h, declaration, payload, uf, true, 0));
                }
                envelope.Add(header);
            }
            envelope.Add(new XElement(soap + "Body", InputElement == payload.Name ? new XElement(payload) : BuildElement(InputElement, input, payload, uf, false, 0)));
            return Safe.Utf8(new XDocument(envelope));
        }
    }

    public interface ISoapTransport
    {
        byte[] Call(string endpoint, string operation, XElement payload, string uf, CancellationToken token);
    }
    public sealed class SoapTransport : ISoapTransport
    {
        readonly X509Certificate2 certificate;
        readonly bool localTest;
        readonly Dictionary<string, SoapContract> contracts = new Dictionary<string, SoapContract>();
        public SoapTransport(X509Certificate2 certificate) { this.certificate = certificate; }
        internal SoapTransport(X509Certificate2 certificate, bool localTest) { this.certificate = certificate; this.localTest = localTest; }
        public static string Endpoint(string uf, int env, bool list)
        {
            if (env != 1 && env != 2) throw new ArgumentException("Ambiente inválido.");
            if (uf == "SP") return "https://" + (env == 2 ? "homologacao." : "") + "nfce.fazenda.sp.gov.br/ws/" + (list ? "NFCeListagemChaves" : "NFCeDownloadXML") + ".asmx";
            if (uf == "RS") return "https://dfe-servico" + (env == 2 ? "-homologacao" : "") + ".svrs.rs.gov.br/WS/NFeIntegracao/NFeIntegracao.asmx";
            throw new ArgumentException("Selecione SP ou RS.");
        }
        public byte[] Call(string endpoint, string operation, XElement payload, string uf, CancellationToken token)
        {
            Uri endpointUri = new Uri(endpoint);
            bool official = new[] { Endpoint("SP",1,true), Endpoint("SP",1,false), Endpoint("SP",2,true), Endpoint("SP",2,false), Endpoint("RS",1,false), Endpoint("RS",2,false) }.Contains(endpoint,StringComparer.OrdinalIgnoreCase);
            if (!official && !(localTest && endpointUri.IsLoopback && endpointUri.Scheme == "https")) throw new InvalidOperationException("Certificado restrito aos serviços oficiais configurados.");
            SoapContract contract;
            if (!contracts.TryGetValue(endpoint, out contract))
            {
                XDocument wsdl = Safe.Xml(Request(new Uri(endpoint + "?WSDL"), null, null, token));
                // ASMX normalmente publica schemas inline. Resolve imports somente no mesmo host HTTPS.
                var seen = new HashSet<string>();
                for (int i = 0; i < 5; i++)
                {
                    XNamespace xs = "http://www.w3.org/2001/XMLSchema";
                    var import = wsdl.Descendants().FirstOrDefault(e => (e.Name == xs + "import" || e.Name == xs + "include") && e.Attribute("schemaLocation") != null && !seen.Contains((string)e.Attribute("schemaLocation")));
                    if (import == null) break;
                    string location = (string)import.Attribute("schemaLocation"); seen.Add(location);
                    Uri address = new Uri(new Uri(endpoint), location);
                    if (address.Scheme != "https" || address.Host != new Uri(endpoint).Host || address.Port != 443)
                        throw new InvalidDataException("O WSDL indicou schema fora do host oficial. Atualize o contrato antes de consultar.");
                    XDocument schema = Safe.Xml(Request(address, null, null, token));
                    if (schema.Root == null || schema.Root.Name != xs + "schema") throw new InvalidDataException("Schema WSDL inválido.");
                    wsdl.Root.Element(XName.Get("types", "http://schemas.xmlsoap.org/wsdl/")).Add(schema.Root);
                }
                contract = SoapContract.Parse(wsdl, operation); contracts.Add(endpoint, contract);
            }
            return Request(new Uri(endpoint), contract.Envelope(payload, uf), contract, token);
        }
        byte[] Request(Uri uri, byte[] bytes, SoapContract contract, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.CheckCertificateRevocationList = true;
            var req = (HttpWebRequest)WebRequest.Create(uri);
            req.AllowAutoRedirect = false; req.Timeout = 60000; req.ReadWriteTimeout = 60000;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            req.ClientCertificates.Add(certificate);
            req.UserAgent = "openNfc-eDownload/1.0";
            if (bytes != null)
            {
                req.Method = "POST";
                if (contract.Action.IndexOfAny(new[] {'\r','\n','"'}) >= 0) throw new InvalidDataException("SOAP action inválida.");
                req.ContentType = contract.Soap12 ? "application/soap+xml; charset=utf-8; action=\"" + contract.Action + "\"" : "text/xml; charset=utf-8";
                if (!contract.Soap12) req.Headers["SOAPAction"] = "\"" + contract.Action + "\"";
                req.ContentLength = bytes.Length;
            }
            using (token.Register(delegate { req.Abort(); }))
            {
                try
                {
                    if (bytes != null) using (var output = req.GetRequestStream()) output.Write(bytes, 0, bytes.Length);
                    using (var response = (HttpWebResponse)req.GetResponse())
                    {
                        if ((int)response.StatusCode >= 300) throw new IOException("O serviço redirecionou a consulta. Confira os endereços oficiais.");
                        using (var input = response.GetResponseStream()) return Safe.Read(input, Safe.XmlLimit);
                    }
                }
                catch (WebException ex)
                {
                    token.ThrowIfCancellationRequested();
                    var response = ex.Response as HttpWebResponse;
                    if (response != null) using (response)
                    {
                        if (bytes != null && response.StatusCode == HttpStatusCode.InternalServerError)
                            using (var input = response.GetResponseStream()) return Safe.Read(input, Safe.XmlLimit);
                        throw new IOException("HTTP " + (int)response.StatusCode + " no serviço oficial. Confira validade do A1, cadeia ICP-Brasil e autorização de acesso.", ex);
                    }
                    throw new IOException("Falha de conexão/TLS com a SEFAZ: " + ex.Status + ". Confira internet, relógio e cadeia ICP-Brasil.", ex);
                }
            }
        }
    }
    public static class SoapResult
    {
        public static XElement Payload(byte[] bytes, string expected)
        {
            XDocument doc = Safe.Xml(bytes);
            var fault = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Fault" &&
                (e.Name.NamespaceName == "http://www.w3.org/2003/05/soap-envelope" || e.Name.NamespaceName == "http://schemas.xmlsoap.org/soap/envelope/"));
            if (fault != null) throw new InvalidDataException("Falha SOAP: " + String.Join(" ", fault.DescendantNodes().OfType<XText>().Select(t => t.Value).ToArray()));
            var results = doc.Descendants(XName.Get(expected, Safe.Ns)).ToList();
            if (results.Count == 1) return results[0];
            // Alguns ASMX serializam Xml como string dentro de ...Result.
            foreach (var e in doc.Descendants().Where(e => !e.HasElements && e.Name.LocalName.EndsWith("Result", StringComparison.Ordinal)))
                if (e.Value.TrimStart().StartsWith("<", StringComparison.Ordinal))
                {
                    XDocument inner = Safe.Xml(Encoding.UTF8.GetBytes(e.Value));
                    if (inner.Root != null && inner.Root.Name == XName.Get(expected, Safe.Ns)) return inner.Root;
                }
            throw new InvalidDataException("Resposta oficial sem " + expected + " único no namespace NF-e. O retorno foi preservado.");
        }
    }
}
