using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace OpenNfceDownload
{
    /// <summary>Limites de entrada e armazenamento; não altera os bytes fiscais recebidos.</summary>
    public static class Safe
    {
        public const string Ns = "http://www.portalfiscal.inf.br/nfe";
        public const int XmlLimit = 20 * 1024 * 1024;
        public const int ResponseLimit = 48 * 1024 * 1024;
        public static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");
        public static string Cnpj(string input)
        {
            string s = Regex.Replace((input ?? "").ToUpperInvariant(), @"[.\-/\s]", "");
            if (!Regex.IsMatch(s, @"^[A-Z0-9]{12}[0-9]{2}$") || s.Distinct().Count() == 1)
                throw new InvalidDataException("Informe um CNPJ válido, com 14 posições.");
            int[][] weights = { new[] {5,4,3,2,9,8,7,6,5,4,3,2}, new[] {6,5,4,3,2,9,8,7,6,5,4,3,2} };
            for (int step = 0; step < 2; step++)
            {
                int sum = 0;
                for (int i = 0; i < 12 + step; i++) sum += (s[i] - 48) * weights[step][i];
                int digit = sum % 11 < 2 ? 0 : 11 - sum % 11;
                if (s[12 + step] - 48 != digit) throw new InvalidDataException("Dígitos verificadores do CNPJ inválidos.");
            }
            return s;
        }
        /// <summary>Modelo 65, CNPJ e módulo 11, inclusive CNPJ alfanumérico.</summary>
        public static bool Key(string value)
        {
            string s = value ?? "";
            if (!Regex.IsMatch(s, @"^[0-9]{6}[A-Z0-9]{12}[0-9]{26}$") || s.Substring(20, 2) != "65") return false;
            if (!new[] {"11","12","13","14","15","16","17","21","22","23","24","25","26","27","28","29","31","32","33","35","41","42","43","50","51","52","53"}.Contains(s.Substring(0,2))) return false;
            int month = Int32.Parse(s.Substring(4,2), CultureInfo.InvariantCulture);
            if (month < 1 || month > 12) return false;
            try { Cnpj(s.Substring(6,14)); } catch (InvalidDataException) { return false; }
            int sum = 0, weight = 2;
            for (int i = 42; i >= 0; i--) { sum += (s[i] - 48) * weight; weight = weight == 9 ? 2 : weight + 1; }
            int digit = sum % 11 < 2 ? 0 : 11 - sum % 11;
            return digit == s[43] - 48;
        }
        public static List<string> Keys(string text, out List<string> invalid)
        {
            var valid = new List<string>(); var seen = new HashSet<string>(); invalid = new List<string>();
            foreach (string part in Regex.Split(text ?? "", @"[\s;,|]+"))
            {
                string s = part.Trim('"', '\'', ' ', '\uFEFF').ToUpperInvariant();
                if (s == "") continue;
                if (s.StartsWith("NFE", StringComparison.Ordinal) && s.Length == 47) s = s.Substring(3);
                if (!Key(s)) { invalid.Add(s.Length > 90 ? s.Substring(0, 90) + "…" : s); continue; }
                if (seen.Add(s)) valid.Add(s);
            }
            return valid;
        }
        public static XDocument Xml(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > XmlLimit) throw new InvalidDataException("XML vazio ou maior que 20 MB.");
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = XmlLimit, MaxCharactersFromEntities = 0 };
            using (var stream = new MemoryStream(bytes)) using (var reader = XmlReader.Create(stream, settings))
                return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        public static byte[] Utf8(XDocument doc) { return new UTF8Encoding(false).GetBytes(doc.ToString(SaveOptions.DisableFormatting)); }
        public static byte[] Read(Stream stream, int limit)
        {
            using (var output = new MemoryStream())
            {
                byte[] buffer = new byte[16384]; int n;
                while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + n > limit) throw new InvalidDataException("Conteúdo excede o limite permitido.");
                    output.Write(buffer, 0, n);
                }
                return output.ToArray();
            }
        }
        public static byte[] ReadFile(string file, int limit)
        {
            using (var stream = File.OpenRead(file)) return Read(stream, limit);
        }
        public static byte[] Unzip(string base64)
        {
            if (String.IsNullOrWhiteSpace(base64) || base64.Length > ResponseLimit) throw new InvalidDataException("ArquivoXml ausente ou muito grande.");
            byte[] compressed = Convert.FromBase64String(base64);
            if (compressed.Length < 2 || compressed[0] != 0x1f || compressed[1] != 0x8b)
                throw new InvalidDataException("O XML da API não está no formato GZip esperado.");
            using (var input = new MemoryStream(compressed)) using (var zip = new GZipStream(input, CompressionMode.Decompress))
                return Read(zip, XmlLimit);
        }
        public static string Hash(byte[] data)
        {
            using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }
        public static void Write(string file, byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
            string tmp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var f = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(data, 0, data.Length); f.Flush(true); }
                if (File.Exists(file)) File.Replace(tmp, file, null); else File.Move(tmp, file);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }
        public static string Value(XElement e, string name) { return e == null ? "" : (string)e.Element(XName.Get(name, Ns)) ?? ""; }
        public static XElement Element(XElement e, string name) { return e == null ? null : e.Element(XName.Get(name, Ns)); }
        public static decimal Money(string value)
        {
            decimal number;
            if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number))
                throw new InvalidDataException("Valor monetário inválido no XML: " + value);
            return number;
        }
        public static string Identifier(XElement e)
        {
            string v = Value(e, "CNPJ"); if (v == "") v = Value(e, "CPF"); if (v == "") v = Value(e, "NIF"); return v;
        }
        public static string Date(DateTime value) { return value.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture); }
        public static DateTime Utc(string value) { return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime(); }
    }

    /// <summary>A1 em sessão: senha não persistida; chave temporária liberada ao encerrar.</summary>
    public sealed class CertificateSession : IDisposable
    {
        public X509Certificate2 Certificate { get; private set; }
        public string Cnpj { get; private set; }
        public string Cpf { get; private set; }
        public string Identity { get { return Cnpj != "" ? Cnpj : Cpf; } }
        public CertificateSession(string path, string password)
        {
            X509Certificate2 cert = null;
            try
            {
                cert = new X509Certificate2(path, password, X509KeyStorageFlags.UserKeySet);
                if (!cert.HasPrivateKey) throw new InvalidOperationException("O A1 precisa conter a chave privada (PFX/P12).");
                if (DateTime.Now < cert.NotBefore || DateTime.Now > cert.NotAfter) throw new InvalidOperationException("Certificado vencido ou ainda não válido.");
                Cnpj = FindCnpj(cert);
                Cpf = FindCpf(cert);
                if (Cnpj == "" && Cpf == "") throw new InvalidOperationException("Selecione um A1 e-CNPJ ou e-CPF com identificação ICP-Brasil.");
                using (var rsa = cert.GetRSAPrivateKey())
                {
                    if (rsa == null) throw new InvalidOperationException("Certificado A1 RSA necessário.");
                    rsa.SignData(new byte[] {1,2,3}, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
                Certificate = cert; cert = null;
            }
            catch (CryptographicException) { throw new InvalidOperationException("Não foi possível abrir o A1. Confira a senha e o PFX/P12."); }
            finally { if (cert != null) cert.Dispose(); }
        }
        public void Validate(string cnpj, string uf)
        {
            if (Certificate == null || DateTime.Now > Certificate.NotAfter || DateTime.Now < Certificate.NotBefore)
                throw new InvalidOperationException("Carregue um certificado A1 válido.");
            string id = Safe.Cnpj(cnpj);
            if (uf == "SP" && (Cnpj == "" || id != Cnpj)) throw new InvalidOperationException("Em SP, use o e-CNPJ do estabelecimento consultado (CNPJ completo idêntico).");
            // No RS a autorização eletrônica é validada pelo serviço, inclusive para e-CPF.
        }
        public static string FindCpf(X509Certificate2 cert)
        {
            foreach (var ext in cert.Extensions) if (ext.Oid.Value == "2.5.29.17")
            {
                byte[] data = ext.RawData;
                foreach (var node in Der.Read(data, 0, data.Length)) foreach (var other in node.Children)
                {
                    if (other.Tag != 0xa0 || other.Children.Count < 2 || other.Children[0].Tag != 6 ||
                        !other.Children[0].Bytes.SequenceEqual(new byte[] {0x60,0x4c,1,3,1})) continue;
                    foreach (var leaf in other.Children[1].Leaves())
                    {
                        // OID 2.16.76.1.3.1: nascimento (8) seguido do CPF (11).
                        string s = Encoding.UTF8.GetString(leaf.Bytes).Trim('\0',' ');
                        if (s.Length < 19) continue;
                        string cpf = s.Substring(8,11);
                        if (ValidCpf(cpf)) return cpf;
                    }
                }
            }
            return "";
        }
        public static bool ValidCpf(string s)
        {
            if (!Regex.IsMatch(s ?? "", @"^[0-9]{11}$") || s.Distinct().Count() == 1) return false;
            for (int n = 9; n <= 10; n++)
            {
                int sum = 0; for (int i = 0; i < n; i++) sum += (s[i]-48)*(n+1-i);
                int digit = sum % 11 < 2 ? 0 : 11 - sum % 11;
                if (s[n]-48 != digit) return false;
            }
            return true;
        }
        public static string FindCnpj(X509Certificate2 cert)
        {
            foreach (var ext in cert.Extensions) if (ext.Oid.Value == "2.5.29.17")
            {
                byte[] data = ext.RawData;
                foreach (var node in Der.Read(data, 0, data.Length)) foreach (var other in node.Children)
                {
                    if (other.Tag != 0xa0 || other.Children.Count < 2) continue;
                    var oid = other.Children[0];
                    if (oid.Tag != 6 || !oid.Bytes.SequenceEqual(new byte[] {0x60,0x4c,1,3,3})) continue;
                    foreach (var leaf in other.Children[1].Leaves())
                    {
                        try { return Safe.Cnpj(Encoding.UTF8.GetString(leaf.Bytes).Trim('\0',' ','\r','\n')); }
                        catch (InvalidDataException) { }
                    }
                }
            }
            return "";
        }
        public void Dispose() { if (Certificate != null) { Certificate.Dispose(); Certificate = null; } }
    }
    internal sealed class Der
    {
        public byte Tag; public byte[] Bytes; public List<Der> Children = new List<Der>();
        public IEnumerable<Der> Leaves() { if (Children.Count == 0) yield return this; else foreach (var c in Children) foreach (var n in c.Leaves()) yield return n; }
        public static List<Der> Read(byte[] d, int start, int end, int depth = 0)
        {
            if (depth > 12) throw new InvalidDataException("Extensão do certificado inválida.");
            var nodes = new List<Der>(); int p = start;
            while (p < end)
            {
                if (p + 2 > end) throw new InvalidDataException("ASN.1 truncado.");
                byte tag = d[p++]; int len = d[p++];
                if (len >= 128)
                {
                    int count = len & 127; len = 0;
                    if (count == 0 || count > 4 || p + count > end) throw new InvalidDataException("ASN.1 inválido.");
                    for (int i = 0; i < count; i++) len = checked(len * 256 + d[p++]);
                }
                if (len < 0 || len > end - p) throw new InvalidDataException("ASN.1 truncado.");
                var node = new Der { Tag = tag, Bytes = d.Skip(p).Take(len).ToArray() };
                if ((tag & 32) != 0) node.Children = Read(d, p, p + len, depth + 1);
                nodes.Add(node); p += len;
            }
            return nodes;
        }
    }
}
