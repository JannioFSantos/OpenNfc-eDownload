using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace OpenNfceDownload
{
    /// <summary>Fixtures inteiramente sintéticas. Nunca lê o A1 ou XMLs do usuário.</summary>
    public static class SelfTests
    {
        const string Company = "11222333000181";
        static readonly XNamespace N = Safe.Ns;
        static int count;
        static readonly List<string> passed = new List<string>();
        static void Check(bool ok, string name) { if (!ok) throw new Exception("Falhou: " + name); count++; passed.Add("OK " + name); }
        static void Reject(Action action, string name)
        {
            bool rejected = false; try { action(); } catch (Exception) { rejected = true; }
            Check(rejected,name);
        }
        public static string Key(int number, string uf = "35")
        {
            string s = uf + DateTime.Today.ToString("yyMM",CultureInfo.InvariantCulture) + Company + "65001" + number.ToString("000000000",CultureInfo.InvariantCulture) + "1" + "12345678";
            int sum = 0, weight = 2;
            for (int i = s.Length-1; i >= 0; i--) { sum += (s[i]-48)*weight; weight = weight == 9 ? 2 : weight+1; }
            return s + (sum%11 < 2 ? 0 : 11-sum%11);
        }
        public static XElement Invoice(int number, string uf, bool simplified)
        {
            string key = Key(number,uf);
            var nfe = new XElement(N+"NFe",new XElement(N+"infNFe",new XAttribute("Id","NFe"+key),new XAttribute("versao","4.00"),
                new XElement(N+"ide",new XElement(N+"cUF",uf),new XElement(N+"mod","65"),new XElement(N+"serie","1"),new XElement(N+"nNF",number),new XElement(N+"dhEmi",DateTime.Today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)+"T10:00:00-03:00"),new XElement(N+"tpAmb","1")),
                new XElement(N+"emit",new XElement(N+"CNPJ",Company),new XElement(N+"xNome","EMITENTE SINTETICO")),
                new XElement(N+"det",new XAttribute("nItem","1"),new XElement(N+"prod",new XElement(N+"cProd","TESTE"),new XElement(N+"xProd","PRODUTO SINTETICO"),new XElement(N+"NCM","00000000"),new XElement(N+"CFOP","5102"),new XElement(N+"uCom","UN"),new XElement(N+"qCom","2.0000"),new XElement(N+"vUnCom","5.0000"),new XElement(N+"vProd","10.00"))),
                new XElement(N+"total",new XElement(N+"ICMSTot",new XElement(N+"vNF","10.00")))));
            var root = new XElement(N+"nfeProc",new XAttribute("versao","4.00"));
            if (simplified) root.Add(new XElement(N+"dhInc",DateTimeOffset.Now.ToString("o")),new XElement(N+"nProt","135260000000001"),nfe);
            else root.Add(nfe,new XElement(N+"protNFe",new XAttribute("versao","4.00"),new XElement(N+"infProt",new XElement(N+"tpAmb","1"),new XElement(N+"chNFe",key),new XElement(N+"nProt","143260000000001"),new XElement(N+"cStat","100"))));
            return root;
        }
        static XElement Event(int number, string uf, bool simplified, string status = "135")
        {
            string key = Key(number,uf);
            var root = new XElement(N+"procEventoNFe",new XAttribute("versao","1.00"));
            if (simplified) root.Add(new XElement(N+"dhInc",DateTimeOffset.Now.ToString("o")),new XElement(N+"nProt","135260000000002"));
            root.Add(new XElement(N+"evento",new XAttribute("versao","1.00"),new XElement(N+"infEvento",new XAttribute("Id","ID110111"+key+"01"),new XElement(N+"tpAmb","1"),new XElement(N+"chNFe",key),new XElement(N+"dhEvento",DateTimeOffset.Now.ToString("o")),new XElement(N+"tpEvento","110111"),new XElement(N+"nSeqEvento","1"))));
            if (!simplified) root.Add(new XElement(N+"retEvento",new XElement(N+"infEvento",new XElement(N+"tpAmb","1"),new XElement(N+"chNFe",key),new XElement(N+"cStat",status),new XElement(N+"tpEvento","110111"),new XElement(N+"nSeqEvento","1"),new XElement(N+"nProt","143260000000002"))));
            return root;
        }
        static byte[] Soap(XElement result)
        { XNamespace s = "http://www.w3.org/2003/05/soap-envelope"; return Safe.Utf8(new XDocument(new XElement(s+"Envelope",new XElement(s+"Body",result)))); }
        static XElement Response(string name, string code, params object[] content)
        { return new XElement(N+name,new XAttribute("versao","1.00"),new XElement(N+"tpAmb","1"),new XElement(N+"verAplic","sintetico"),new XElement(N+"cStat",code),new XElement(N+"xMotivo","Retorno sintetico"),content); }
        static string Gzip(XElement xml)
        {
            using (var output = new MemoryStream())
            {
                using (var zip = new GZipStream(output,CompressionMode.Compress,true)) { byte[] bytes = Safe.Utf8(new XDocument(xml)); zip.Write(bytes,0,bytes.Length); }
                return Convert.ToBase64String(output.ToArray());
            }
        }
        sealed class Fake : ISoapTransport
        {
            public readonly List<XElement> Requests = new List<XElement>();
            public readonly Queue<Func<XElement,byte[]>> Replies = new Queue<Func<XElement,byte[]>>();
            public byte[] Call(string endpoint, string operation, XElement payload, string uf, CancellationToken token)
            { token.ThrowIfCancellationRequested(); Requests.Add(new XElement(payload)); if (Replies.Count == 0) throw new IOException("Sem resposta sintética."); return Replies.Dequeue()(payload); }
            public void Add(XElement result) { Replies.Enqueue(delegate { return Soap(result); }); }
        }
        static RunSettings Settings(string root, string name, string uf = "SP")
        { return new RunSettings { Root = Path.Combine(root,name), Uf = uf, Cnpj = Company, Start = DateTime.Today, End = DateTime.Today.AddMinutes(2) }; }
        static XElement RsResponse(long next, params XElement[] procs)
        { return Response("retDistNFeRS",procs.Length == 0 ? "117" : "118",new XElement(N+"CNPJ",Company),new XElement(N+"mod","65"),new XElement(N+"ultNSURet",next),procs.Length == 0 ? null : new XElement(N+"loteDistComp",Gzip(new XElement(N+"loteDistNFeRS",new XAttribute("versao","1.00"),procs)))); }
        static XElement Proc(long nsu, int number, XElement doc)
        { return new XElement(N+"proc",new XAttribute("NSU",nsu),new XAttribute("chAcesso",Key(number,"43")),new XAttribute("schema",doc.Name.LocalName+"_v4.00.xsd"),doc); }
        public static XDocument Wsdl(bool wrapped, bool soap12)
        {
            XNamespace w = "http://schemas.xmlsoap.org/wsdl/", x = "http://www.w3.org/2001/XMLSchema", b = soap12 ? "http://schemas.xmlsoap.org/wsdl/soap12/" : "http://schemas.xmlsoap.org/wsdl/soap/", t = "urn:synthetic:soap";
            var schema = new XElement(x+"schema",new XAttribute("targetNamespace",t),new XAttribute("elementFormDefault","qualified"));
            var data = new XElement(x+"element",new XAttribute("name","nfeDadosMsg"),new XElement(x+"complexType",new XElement(x+"sequence",new XElement(x+"any",new XAttribute("processContents","lax")))));
            if (wrapped) schema.Add(new XElement(x+"element",new XAttribute("name","nfeIntegracaoContab"),new XElement(x+"complexType",new XElement(x+"sequence",data)))); else schema.Add(data);
            return new XDocument(new XElement(w+"definitions",new XAttribute("targetNamespace",t),new XAttribute(XNamespace.Xmlns+"tns",t),new XAttribute(XNamespace.Xmlns+"xsd",x),
                new XElement(w+"types",schema),new XElement(w+"message",new XAttribute("name","Input"),new XElement(w+"part",new XAttribute("name","parameters"),new XAttribute("element",wrapped ? "tns:nfeIntegracaoContab" : "tns:nfeDadosMsg"))),
                new XElement(w+"portType",new XAttribute("name","Port"),new XElement(w+"operation",new XAttribute("name","nfeIntegracaoContab"),new XElement(w+"input",new XAttribute("message","tns:Input")))),
                new XElement(w+"binding",new XAttribute("name","Binding"),new XAttribute("type","tns:Port"),new XElement(b+"binding",new XAttribute("transport","http://schemas.xmlsoap.org/soap/http")),new XElement(w+"operation",new XAttribute("name","nfeIntegracaoContab"),new XElement(b+"operation",new XAttribute("soapAction","urn:synthetic:soap/nfeIntegracaoContab")),new XElement(w+"input",new XElement(b+"body",new XAttribute("use","literal")))))));
        }
        static void Basics(string root)
        {
            Check(Safe.Cnpj("11.222.333/0001-81") == Company,"CNPJ numérico validado");
            Reject(delegate { Safe.Cnpj("11222333000182"); },"rejeita CNPJ com DV inválido");
            Check(Safe.Key(Key(1)) && Safe.Key(Key(1,"43")),"chaves SP e RS modelo 65");
            Check(!Safe.Key(Key(1).Substring(0,43)+((Key(1)[43]-48+1)%10)),"DV da chave conferido");
            Check(!Safe.Key(Key(1).Substring(0,20)+"55"+Key(1).Substring(22)),"rejeita modelo 55");
            List<string> invalid; var parsed = Safe.Keys(Key(1)+"\t"+Key(2)+"\r\nNFe"+Key(1),out invalid);
            Check(parsed.Count == 2 && invalid.Count == 0,"cola planilha, prefixo NFe e remove duplicatas");
            Safe.Keys("3.52610E+43",out invalid); Check(invalid.Count == 1,"rejeita notação científica da planilha");
            Reject(delegate { Safe.Xml(Encoding.UTF8.GetBytes("<!DOCTYPE r [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]><r>&x;</r>")); },"DTD e entidade externa proibidos");
            Reject(delegate { Safe.Unzip(Convert.ToBase64String(Encoding.UTF8.GetBytes("texto"))); },"rejeita lote sem GZip");
            Check(Safe.Xml(Safe.Unzip(Gzip(new XElement(N+"teste")))).Root.Name == N+"teste","GZip base64 descompactado");
            string f = Path.Combine(root,"atomic.xml"); Safe.Write(f,new byte[] {1}); Safe.Write(f,new byte[] {2}); Check(File.ReadAllBytes(f).SequenceEqual(new byte[] {2}),"substituição atômica em disco");
            Check(SoapTransport.Endpoint("SP",2,true).Contains("homologacao") && SoapTransport.Endpoint("RS",1,false).Contains("dfe-servico.svrs.rs.gov.br"),"endpoints oficiais atuais por ambiente");
            Reject(delegate { SoapTransport.Endpoint("CE",1,false); },"UF não suportada rejeitada");
            Check(LibraryDocument.Parse(new XDocument(Invoice(1,"35",true))).Products.Count == 1,"XML SP simplificado contém produtos");
            Check(LibraryDocument.Parse(new XDocument(Invoice(1,"43",false))).Format == "nfeProc padrão","XML RS padrão com protocolo");
            XElement bad = Invoice(1,"35",true); bad.Descendants(N+"mod").Single().Value = "55";
            Reject(delegate { LibraryDocument.Parse(new XDocument(bad)); },"biblioteca não aceita NF-e como NFC-e");
            bad = Invoice(1,"35",true); bad.Descendants(N+"det").Remove();
            Reject(delegate { LibraryDocument.Parse(new XDocument(bad)); },"resumo sem produtos não é download completo");
            Check(LibraryDocument.Parse(new XDocument(Event(1,"35",true))).Cancellation,"cancelamento simplificado SP");
            Check(!LibraryDocument.Parse(new XDocument(Event(1,"43",false,"573"))).Cancellation,"evento rejeitado não cancela nota");
            foreach (bool wrapped in new[] {false,true}) foreach (bool soap12 in new[] {false,true})
            {
                var c = SoapContract.Parse(Wsdl(wrapped,soap12),"nfeIntegracaoContab");
                var envelope = Safe.Xml(c.Envelope(DownloadEngine.SpKeyRequest(1,Key(1)),"35"));
                Check(c.Soap12 == soap12 && envelope.Descendants(N+"nfceDownloadXML").Count() == 1 && c.Action == "urn:synthetic:soap/nfeIntegracaoContab","WSDL determina action e envelope " + wrapped + "/" + soap12);
            }
            XNamespace soap = "http://www.w3.org/2003/05/soap-envelope";
            Reject(delegate { SoapResult.Payload(Soap(new XElement(soap+"Fault",new XElement(soap+"Reason","teste"))),"retDistNFeRS"); },"SOAP Fault não é tratado como lote vazio");
            byte[] escaped = Soap(new XElement("operationResult",Response("retDistNFeRS","117").ToString(SaveOptions.DisableFormatting)));
            Check(Safe.Value(SoapResult.Payload(escaped,"retDistNFeRS"),"cStat") == "117","ASMX com XML escapado em Result");
        }
        static void Schemas(string root)
        {
            string schemas = root; if (!Directory.Exists(schemas)) return;
            var set = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
            set.Add(Safe.Ns,Path.Combine(schemas,"sp-xsd","nfceListagemChaves_100.xsd"));
            set.Add(Safe.Ns,Path.Combine(schemas,"sp-xsd","nfceDownloadXML_100.xsd"));
            set.Add(Safe.Ns,Path.Combine(schemas,"rs-oficial","distNFeRS_v1.00.xsd")); set.Compile();
            var s = Settings(Path.GetTempPath(),"schema","RS");
            foreach (var xml in new[] { DownloadEngine.SpListRequest(1,s.Start,s.End),DownloadEngine.SpKeyRequest(1,Key(1)),DownloadEngine.RsRequest(s,0,null),DownloadEngine.RsRequest(s,0,Key(1,"43")) })
            {
                new XDocument(xml).Validate(set,delegate(object sender,ValidationEventArgs e) { throw e.Exception; }); Check(true,"requisição válida no XSD oficial: "+xml.Name.LocalName+(xml.Element(N+"solDFe") != null ? " por chave" : ""));
            }
        }
        static void Downloads(string root)
        {
            var s = Settings(root,"sp-split"); var fake = new Fake();
            fake.Add(Response("retNfceListagemChaves","101",new XElement(N+"chNFCe",Key(1))));
            fake.Add(Response("retNfceListagemChaves","100",new XElement(N+"chNFCe",Key(1))));
            fake.Add(Response("retNfceListagemChaves","100",new XElement(N+"chNFCe",Key(2))));
            fake.Add(Response("retNfceDownloadXML","200",new XElement(N+"proc",Invoice(1,"35",true),Event(1,"35",true))));
            fake.Add(Response("retNfceDownloadXML","200",new XElement(N+"proc",Invoice(2,"35",true))));
            var engine = new DownloadEngine(s,fake,null,0); engine.Sync(CancellationToken.None);
            Check(fake.Requests.Count == 5 && engine.SavedCount == 3,"SP 101 divide período, deduplica e baixa XMLs/eventos");
            Check(new DownloadState(s).Items.Count(i => i.Status == "Baixado") == 2,"fila SP persistida após baixar");
            Check(Directory.GetFiles(s.ContextPath,"*.xml",SearchOption.AllDirectories).Count(p => p.Contains("\\Retornos\\")) == 5,"retornos SOAP originais preservados");
            var lib = new DocumentLibrary(); lib.Load(s.Root); var rows = lib.Filter(Company,1,"Todas",DateTime.Today,DateTime.Today.AddDays(1),"",0,0);
            Check(rows.Count == 2 && rows.Count(r => r.Situacao == "Cancelada") == 1,"biblioteca associa cancelamento sem alterar a nota");
            Check(lib.Filter(Company,1,"SP",DateTime.Today,DateTime.Today.AddDays(1),"PRODUTO SINTETICO",0,1).Count == 1,"filtro por produto, UF, data e situação");
            string csv = Path.Combine(root,"analise.csv"); DocumentLibrary.ExportCsv(csv,rows); Check(File.ReadAllText(csv).Contains("'"+Key(1)),"CSV preserva chave como texto");
            string zipfile = Path.Combine(root,"export.zip"); lib.ExportZip(zipfile,rows);
            using (var file = File.OpenRead(zipfile)) using (var zip = new ZipArchive(file,ZipArchiveMode.Read)) Check(zip.Entries.Count == 3,"ZIP contém somente notas filtradas e seus eventos");
            string imported = Path.Combine(root,"import"); Check(DocumentLibrary.Import(imported,new[] {zipfile},delegate { }) == 3,"importa XMLs/ZIP sem extrair caminhos arbitrários");
            var overflow = Settings(root,"sp-overflow"); overflow.End = overflow.Start; fake = new Fake();
            fake.Add(Response("retNfceListagemChaves","101",new XElement(N+"chNFCe",Key(1)))); fake.Add(Response("retNfceDownloadXML","200",new XElement(N+"proc",Invoice(1,"35",true))));
            var overflowEngine = new DownloadEngine(overflow,fake,null,0);
            Reject(delegate { overflowEngine.Sync(CancellationToken.None); },"SP 101 no mesmo minuto informa período incompleto");
            Check(new DownloadState(overflow).Items[0].Status == "Baixado","chaves conhecidas preservadas e baixadas mesmo com intervalo incompleto");
            var pause = Settings(root,"pause"); fake = new Fake(); fake.Add(Response("retNfceDownloadXML","656")); engine = new DownloadEngine(pause,fake,null,0); engine.Enqueue(new[] {Key(1)},true);
            Reject(delegate { engine.DownloadQueue(CancellationToken.None); },"656 interrompe imediatamente");
            var noNetwork = new Fake(); engine = new DownloadEngine(pause,noNetwork,null,0);
            Reject(delegate { engine.DownloadQueue(CancellationToken.None); },"656 continua bloqueado após reabrir"); Check(noNetwork.Requests.Count == 0,"pausa persistida impede novas chamadas");
            var missing = Settings(root,"missing"); fake = new Fake(); fake.Add(Response("retNfceDownloadXML","200")); engine = new DownloadEngine(missing,fake,null,0); engine.Enqueue(new[] {Key(1)},true);
            Reject(delegate { engine.DownloadQueue(CancellationToken.None); },"sucesso sem XML é falha"); Check(new DownloadState(missing).Items[0].Status == "Pendente","chave sem XML não marcada como baixada");
            var rejected = Settings(root,"rejected"); fake = new Fake(); fake.Add(Response("retNfceDownloadXML","205")); engine = new DownloadEngine(rejected,fake,null,0); engine.Enqueue(new[] {Key(1)},true); engine.DownloadQueue(CancellationToken.None);
            Check(new DownloadState(rejected).Items[0].Status == "Erro","rejeição por chave fica visível na fila");
            var wrong = Settings(root,"wrong"); fake = new Fake(); XElement wrongEnv = Response("retNfceDownloadXML","200",new XElement(N+"proc",Invoice(1,"35",true))); wrongEnv.Element(N+"tpAmb").Value = "2"; fake.Add(wrongEnv);
            engine = new DownloadEngine(wrong,fake,null,0); engine.Enqueue(new[] {Key(1)},true); Reject(delegate { engine.DownloadQueue(CancellationToken.None); },"retorno de outro ambiente rejeitado");
            Reject(delegate { engine.CheckKey(Key(1,"43")); },"SP não consulta chave de outro estado");
            var rs = Settings(root,"rs","RS"); fake = new Fake();
            var full = Enumerable.Range(1,50).Select(i => Proc(i,i,Invoice(i,"43",false))).ToArray(); fake.Add(RsResponse(50,full)); fake.Add(RsResponse(51,Proc(51,51,Invoice(51,"43",false))));
            engine = new DownloadEngine(rs,fake,null,0); engine.Sync(CancellationToken.None);
            Check(new DownloadState(rs).Cursor == 51 && engine.SavedCount == 51,"RS baixa 50+1 documentos e persiste NSU");
            Check(Safe.Value(fake.Requests[1].Element(N+"solRel"),"ultNSU") == "50","próximo lote começa após NSU anterior");
            Check(new DownloadState(rs).NotBefore > DateTime.UtcNow.AddMinutes(14),"fim da distribuição RS exige pausa de 15min");
            noNetwork = new Fake(); engine = new DownloadEngine(rs,noNetwork,null,0); Reject(delegate { engine.Sync(CancellationToken.None); },"RS respeita pausa após reabrir"); Check(noNetwork.Requests.Count == 0,"sem consumo durante pausa RS");
            rs.Direction = 2; noNetwork = new Fake(); engine = new DownloadEngine(rs,noNetwork,null,0); Reject(delegate { engine.Sync(CancellationToken.None); },"RS pausa compartilhada entre filtros do mesmo CNPJ/modelo"); Check(noNetwork.Requests.Count == 0,"trocar direção não contorna pausa de distribuição");
            var partial = Settings(root,"partial","RS"); fake = new Fake();
            XElement broken = Invoice(2,"43",false); broken.Descendants(N+"det").Remove(); fake.Add(RsResponse(2,Proc(1,1,Invoice(1,"43",false)),Proc(2,2,broken)));
            engine = new DownloadEngine(partial,fake,null,0); Reject(delegate { engine.Sync(CancellationToken.None); },"XML inválido em lote RS interrompe"); Check(new DownloadState(partial).Cursor == 0,"lote parcialmente gravado nunca avança NSU");
            var regress = Settings(root,"regress","RS"); var state = new DownloadState(regress); state.Cursor = 10; state.Save(); fake = new Fake(); fake.Add(RsResponse(9)); engine = new DownloadEngine(regress,fake,null,0);
            Reject(delegate { engine.Sync(CancellationToken.None); },"NSU regressivo rejeitado"); Check(new DownloadState(regress).Cursor == 10,"cursor anterior mantido em regressão");
            var direct = Settings(root,"rs-key","RS"); fake = new Fake(); fake.Add(RsResponse(1,Proc(1,1,Invoice(1,"43",false)))); engine = new DownloadEngine(direct,fake,null,0); engine.Enqueue(new[] {Key(1,"43")},true); engine.DownloadQueue(CancellationToken.None);
            Check(engine.State.Items[0].Status == "Baixado" && new DownloadState(direct).Cursor == 0,"RS baixa por chave sem alterar cursor da distribuição");
            var cancelled = new CancellationTokenSource(); cancelled.Cancel(); fake = new Fake(); engine = new DownloadEngine(Settings(root,"cancel"),fake,null,0);
            Reject(delegate { engine.Sync(cancelled.Token); },"cancelamento respeitado antes da rede"); Check(fake.Requests.Count == 0,"consulta cancelada não acessa serviço");
        }
        static void MutualTls(string path)
        {
            using (var session = new CertificateSession(path,"somente-teste"))
            {
                Check(session.Cnpj == Company && session.Certificate.HasPrivateKey,"A1 sintético com SAN ICP-Brasil e chave privada"); session.Validate(Company,"SP");
                session.Validate("19131243000197","RS"); Check(true,"RS permite CNPJ de cliente diferente e delega autorização ao serviço");
                Reject(delegate { session.Validate("19131243000197","SP"); },"SP exige CNPJ completo do A1");
                Check(CertificateSession.ValidCpf("11144477735") && !CertificateSession.ValidCpf("11144477736"),"CPF sintético validado");
                Reject(delegate { new SoapTransport(session.Certificate).Call("https://example.com/","x",new XElement("x"),"35",CancellationToken.None); },"A1 não é enviado para host arbitrário");
                Reject(delegate { new SoapTransport(session.Certificate).Call("http://localhost/","x",new XElement("x"),"35",CancellationToken.None); },"A1 não é enviado sem HTTPS");
                var listener = new TcpListener(IPAddress.Loopback,0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var original = ServicePointManager.ServerCertificateValidationCallback; var cert = session.Certificate;
                int mutual = 0; bool gotPost = false;
                // Somente o teste aceita o seu próprio certificado sintético em loopback.
                ServicePointManager.ServerCertificateValidationCallback = delegate(object sender,X509Certificate remote,X509Chain chain,SslPolicyErrors errors)
                { var req = sender as HttpWebRequest; return req != null && req.RequestUri.IsLoopback && remote != null && remote.GetCertHashString() == cert.Thumbprint; };
                try
                {
                    ServicePointManager.Expect100Continue = false;
                    var server = Task.Run(delegate
                    {
                        for (int i = 0; i < 2; i++) using (var tcp = listener.AcceptTcpClient()) using (var ssl = new SslStream(tcp.GetStream(),false,delegate(object sender,X509Certificate remote,X509Chain chain,SslPolicyErrors errors) { return remote != null && remote.GetCertHashString() == cert.Thumbprint; }))
                        {
                            ssl.ReadTimeout = 10000; ssl.WriteTimeout = 10000; ssl.AuthenticateAsServer(cert,true,SslProtocols.Tls12,false); if (ssl.IsMutuallyAuthenticated) mutual++;
                            var header = new StringBuilder(); while (!header.ToString().EndsWith("\r\n\r\n")) { int b = ssl.ReadByte(); if (b < 0 || header.Length > 16000) throw new IOException("HTTP sintético inválido."); header.Append((char)b); }
                            string headers = header.ToString(); int size = 0;
                            foreach (string line in headers.Split(new[] {"\r\n"},StringSplitOptions.None)) if (line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase)) size = Int32.Parse(line.Substring(15).Trim(),CultureInfo.InvariantCulture);
                            byte[] body = new byte[size]; int offset = 0; while (offset < size) { int n = ssl.Read(body,offset,size-offset); if (n <= 0) throw new IOException("Corpo truncado."); offset += n; }
                            byte[] reply;
                            if (i == 0) { if (!headers.StartsWith("GET /service?WSDL ",StringComparison.Ordinal)) throw new IOException("Esperava WSDL."); reply = Safe.Utf8(Wsdl(true,true)); }
                            else { gotPost = headers.StartsWith("POST /service ",StringComparison.Ordinal) && headers.Contains("application/soap+xml") && headers.Contains("urn:synthetic:soap/nfeIntegracaoContab") && Safe.Xml(body).Descendants(N+"distNFeRS").Count() == 1; reply = Soap(Response("retDistNFeRS","117")); }
                            byte[] prefix = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/soap+xml\r\nContent-Length: "+reply.Length+"\r\nConnection: close\r\n\r\n"); ssl.Write(prefix,0,prefix.Length); ssl.Write(reply,0,reply.Length); ssl.Flush();
                        }
                    });
                    var s = Settings(Path.GetTempPath(),"tls","RS");
                    byte[] result = new SoapTransport(cert,true).Call("https://localhost:"+port+"/service","nfeIntegracaoContab",DownloadEngine.RsRequest(s,0,null),"43",CancellationToken.None);
                    if (!server.Wait(15000)) throw new IOException("Servidor de teste não encerrou.");
                    Check(mutual == 2,"mTLS real apresenta A1 no WSDL e no SOAP");
                    Check(gotPost && Safe.Value(SoapResult.Payload(result,"retDistNFeRS"),"cStat") == "117","HTTPS real resolve WSDL e envia SOAP action/payload corretos");
                }
                finally { ServicePointManager.ServerCertificateValidationCallback = original; listener.Stop(); }
            }
        }
        static IEnumerable<Control> Controls(Control root)
        { foreach (Control c in root.Controls) { yield return c; foreach (var child in Controls(c)) yield return child; } }
        static void UiSmoke(string root)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            using (var form = new MainForm(root))
            {
                form.Show(); Application.DoEvents(); form.SmokeTest();
                Check(Controls(form).OfType<TabControl>().Single().TabCount == 5,"interface abre cinco abas e troca SP/RS");
                Check(Controls(form).OfType<DataGridView>().Count() == 3,"interface contém fila, notas e produtos");
                form.Size = form.MinimumSize; Application.DoEvents(); form.SmokeTest(); Check(true,"interface abre no tamanho mínimo"); form.Close(); Application.DoEvents();
            }
        }
        static string Arg(string[] args,string name) { int at = Array.IndexOf(args,name); return at >= 0 && at+1 < args.Length ? args[at+1] : null; }
        public static int Run(string[] args)
        {
            string report = Arg(args,"--report") ?? Path.Combine(Path.GetTempPath(),"openNfce-testes.txt");
            string root = Path.Combine(Path.GetTempPath(),"openNfce-offline-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                Basics(root); string schemas = Arg(args,"--schemas"); if (schemas != null) Schemas(schemas); Downloads(root);
                string cert = Arg(args,"--test-cert"); if (cert != null) MutualTls(cert);
                if (Array.IndexOf(args,"--ui-smoke") >= 0) UiSmoke(Path.Combine(root,"ui"));
                File.WriteAllText(report,"APROVADO — "+count+" verificações offline.\r\n"+String.Join("\r\n",passed)+"\r\nFixtures sintéticas: "+root+"\r\nNenhum serviço fiscal foi consultado.",Encoding.UTF8); return 0;
            }
            catch (Exception ex) { File.WriteAllText(report,"FALHOU após "+count+" verificações.\r\n"+String.Join("\r\n",passed)+"\r\n"+ex+"\r\nFixtures: "+root,Encoding.UTF8); return 1; }
        }
    }
}
