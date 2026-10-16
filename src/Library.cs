using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace OpenNfceDownload
{
    public sealed class ProductItem
    {
        public string Number { get; set; }
        public string Code { get; set; }
        public string Description { get; set; }
        public string Ncm { get; set; }
        public string Cfop { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Total { get; set; }
    }
    public sealed class LibraryDocument
    {
        static readonly XNamespace N = Safe.Ns;
        public string Key, Issuer, IssuerName, Recipient, RecipientName, Number, Series, Uf, Protocol, Format, Status;
        public string FilePath, EventType, Sequence;
        public int Environment;
        public DateTime Date;
        public decimal Total;
        public bool IsEvent, Cancellation;
        public readonly List<ProductItem> Products = new List<ProductItem>();
        static DateTime ReadDate(string value)
        {
            DateTimeOffset date;
            if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) throw new InvalidDataException("XML sem data válida.");
            return date.DateTime;
        }
        public static LibraryDocument Parse(XDocument xml)
        {
            XElement root = xml.Root;
            if (root == null || root.Name.NamespaceName != Safe.Ns) throw new InvalidDataException("Documento fora do namespace NF-e.");
            var result = new LibraryDocument();
            if (root.Name == N + "nfeProc" || root.Name == N + "NFe")
            {
                XElement nfe = root.Name == N + "NFe" ? root : Safe.Element(root, "NFe");
                if (nfe == null || (root.Name == N + "nfeProc" && root.Elements(N + "NFe").Count() != 1)) throw new InvalidDataException("XML sem NFe única.");
                XElement inf = Safe.Element(nfe, "infNFe"), ide = Safe.Element(inf, "ide"), emit = Safe.Element(inf, "emit"), dest = Safe.Element(inf, "dest");
                string id = inf == null ? "" : (string)inf.Attribute("Id") ?? "";
                result.Key = id.StartsWith("NFe", StringComparison.Ordinal) ? id.Substring(3) : "";
                if (!Safe.Key(result.Key) || Safe.Value(ide, "mod") != "65") throw new InvalidDataException("Use somente XML de NFC-e modelo 65 com chave válida.");
                result.Issuer = Safe.Cnpj(Safe.Value(emit, "CNPJ"));
                if (result.Key.Substring(6,14) != result.Issuer || Safe.Value(ide, "cUF") != result.Key.Substring(0,2)) throw new InvalidDataException("Emitente/UF divergem da chave do XML.");
                result.Uf = result.Key.Substring(0,2) == "35" ? "SP" : result.Key.Substring(0,2) == "43" ? "RS" : result.Key.Substring(0,2);
                result.IssuerName = Safe.Value(emit, "xNome"); result.Recipient = Safe.Identifier(dest); result.RecipientName = Safe.Value(dest, "xNome");
                result.Number = Safe.Value(ide, "nNF"); result.Series = Safe.Value(ide, "serie");
                if (result.Key.Substring(25,9).TrimStart('0') != result.Number.TrimStart('0') || result.Key.Substring(22,3).TrimStart('0') != result.Series.TrimStart('0'))
                    throw new InvalidDataException("Número/série divergem da chave do XML.");
                string env = Safe.Value(ide, "tpAmb");
                if (env != "1" && env != "2") throw new InvalidDataException("XML com ambiente inválido.");
                result.Environment = Int32.Parse(env, CultureInfo.InvariantCulture);
                string date = Safe.Value(ide, "dhEmi"); if (date == "") date = Safe.Value(ide, "dEmi");
                result.Date = ReadDate(date);
                if (result.Date.ToString("yyMM", CultureInfo.InvariantCulture) != result.Key.Substring(2,4)) throw new InvalidDataException("Data de emissão diverge da chave.");
                result.Total = Safe.Money(Safe.Value(Safe.Element(Safe.Element(inf, "total"), "ICMSTot"), "vNF"));
                XElement prot = Safe.Element(Safe.Element(root, "protNFe"), "infProt");
                result.Protocol = prot == null ? Safe.Value(root, "nProt") : Safe.Value(prot, "nProt");
                if (prot != null)
                {
                    if (Safe.Value(prot, "chNFe") != result.Key || Safe.Value(prot, "tpAmb") != env) throw new InvalidDataException("Protocolo com chave/ambiente diferentes.");
                    result.Format = "nfeProc padrão";
                    string code = Safe.Value(prot, "cStat"); result.Status = code == "100" || code == "150" ? "Autorizada" : "Protocolo " + code;
                }
                else if (result.Protocol != "") { result.Format = "SEFAZ/SP • protocolo simplificado"; result.Status = "Autorizada (SP)"; }
                else { result.Format = "NFe sem protocolo"; result.Status = "Sem protocolo"; }
                foreach (var det in inf.Elements(N + "det"))
                {
                    XElement p = Safe.Element(det, "prod"); if (p == null) throw new InvalidDataException("Item sem grupo prod.");
                    result.Products.Add(new ProductItem { Number = (string)det.Attribute("nItem") ?? "", Code = Safe.Value(p, "cProd"),
                        Description = Safe.Value(p, "xProd"), Ncm = Safe.Value(p, "NCM"), Cfop = Safe.Value(p, "CFOP"), Unit = Safe.Value(p, "uCom"),
                        Quantity = Safe.Money(Safe.Value(p, "qCom")), UnitPrice = Safe.Money(Safe.Value(p, "vUnCom")), Total = Safe.Money(Safe.Value(p, "vProd")) });
                }
                if (result.Products.Count == 0) throw new InvalidDataException("NFC-e sem produtos: não é XML completo de venda.");
            }
            else if (root.Name == N + "procEventoNFe")
            {
                XElement info = Safe.Element(Safe.Element(root, "evento"), "infEvento");
                XElement ret = Safe.Element(Safe.Element(root, "retEvento"), "infEvento");
                result.Key = Safe.Value(info, "chNFe");
                if (!Safe.Key(result.Key)) throw new InvalidDataException("Evento sem chave NFC-e válida.");
                string env = Safe.Value(info, "tpAmb");
                if (env != "1" && env != "2") throw new InvalidDataException("Evento com ambiente inválido.");
                result.Environment = Int32.Parse(env, CultureInfo.InvariantCulture);
                result.IsEvent = true; result.EventType = Safe.Value(info, "tpEvento"); result.Sequence = Safe.Value(info, "nSeqEvento");
                if (!System.Text.RegularExpressions.Regex.IsMatch(result.EventType + "/" + result.Sequence, @"^[0-9]{6}/[0-9]{1,2}$")) throw new InvalidDataException("Tipo/sequência do evento inválidos.");
                result.Date = ReadDate(Safe.Value(info, "dhEvento"));
                if (ret != null && (Safe.Value(ret, "chNFe") != result.Key || Safe.Value(ret, "tpAmb") != env || Safe.Value(ret, "tpEvento") != result.EventType || Safe.Value(ret, "nSeqEvento") != result.Sequence))
                    throw new InvalidDataException("Retorno de evento incompatível com o pedido.");
                result.Protocol = ret == null ? Safe.Value(root, "nProt") : Safe.Value(ret, "nProt");
                string code = Safe.Value(ret, "cStat");
                result.Cancellation = (result.EventType == "110111" || result.EventType == "110112") && result.Protocol != "" && (ret == null || code == "135" || code == "155");
                result.Format = ret == null ? "Evento SP simplificado" : "procEventoNFe padrão";
                result.Status = result.Cancellation ? "Cancelamento registrado" : "Evento " + result.EventType;
            }
            else throw new InvalidDataException("Tipo XML não suportado: " + root.Name.LocalName + ".");
            return result;
        }
    }
    public sealed class LibraryRow
    {
        public string Chave { get; set; }
        public DateTime Emissao { get; set; }
        public string Numero { get; set; }
        public string Serie { get; set; }
        public string UF { get; set; }
        public string Direcao { get; set; }
        public string Situacao { get; set; }
        public string Emitente { get; set; }
        public string NomeEmitente { get; set; }
        public string Destinatario { get; set; }
        public decimal Valor { get; set; }
        public int Itens { get; set; }
        public string Formato { get; set; }
        public LibraryDocument Document;
    }
    public sealed class DocumentLibrary
    {
        public readonly List<LibraryDocument> Documents = new List<LibraryDocument>();
        public readonly List<string> Warnings = new List<string>();
        public void Load(string root)
        {
            Documents.Clear(); Warnings.Clear();
            string folder = Path.Combine(root, "Documentos");
            if (!Directory.Exists(folder)) return;
            foreach (string path in Directory.EnumerateFiles(folder, "*.xml", SearchOption.AllDirectories))
            {
                string relative = path.Substring(folder.Length).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                if (!(relative.Contains("\\Notas\\") || relative.Contains("\\Eventos\\"))) continue;
                try
                {
                    var doc = LibraryDocument.Parse(Safe.Xml(Safe.ReadFile(path, Safe.XmlLimit))); doc.FilePath = path; Documents.Add(doc);
                }
                catch (Exception e)
                {
                    if (!(e is InvalidDataException || e is System.Xml.XmlException || e is IOException || e is UnauthorizedAccessException || e is FormatException)) throw;
                    Warnings.Add(Path.GetFileName(path) + " • " + e.Message);
                }
            }
        }
        public List<LibraryRow> Filter(string cnpj, int env, string uf, DateTime start, DateTime end, string search, int direction, int status)
        {
            var available = Documents.Where(d => d.Environment == env).ToList();
            var cancelled = new HashSet<string>(available.Where(d => d.IsEvent && d.Cancellation).Select(d => d.Key));
            var rows = new List<LibraryRow>();
            foreach (var doc in available.Where(d => !d.IsEvent && d.Date >= start && d.Date <= end))
            {
                string dir = doc.Issuer == cnpj ? "Emitida" : doc.Recipient == cnpj ? "Recebida" : "Outra";
                if (cnpj != "" && dir == "Outra") continue;
                if (direction == 1 && dir != "Emitida" || direction == 2 && dir != "Recebida") continue;
                if (uf != "Todas" && uf != "" && doc.Uf != uf) continue;
                bool cancel = cancelled.Contains(doc.Key);
                if (status == 1 && cancel || status == 2 && !cancel) continue;
                if (!String.IsNullOrWhiteSpace(search) && (doc.Key + " " + doc.Number + " " + doc.Series + " " + doc.Issuer + " " + doc.IssuerName + " " + doc.Recipient + " " + doc.RecipientName + " " + String.Join(" ", doc.Products.Select(p => p.Description).ToArray())).IndexOf(search.Trim(), StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                rows.Add(new LibraryRow { Chave = doc.Key, Emissao = doc.Date, Numero = doc.Number, Serie = doc.Series, UF = doc.Uf,
                    Direcao = dir, Situacao = cancel ? "Cancelada" : doc.Status, Emitente = doc.Issuer, NomeEmitente = doc.IssuerName,
                    Destinatario = doc.Recipient, Valor = doc.Total, Itens = doc.Products.Count, Formato = doc.Format, Document = doc });
            }
            // Mesmo XML pode ter sido recebido por contextos diferentes. Não duplica totais na análise.
            return rows.GroupBy(r => r.Chave).Select(g => g.OrderByDescending(r => r.Document.Format == "nfeProc padrão").First()).OrderByDescending(r => r.Emissao).ThenBy(r => r.Chave).ToList();
        }
        public static int Import(string root, IEnumerable<string> inputs, Action<string> log)
        {
            int count = 0;
            foreach (string input in inputs)
            {
                if (Path.GetExtension(input).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using (var stream = File.OpenRead(input)) using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                    {
                        if (zip.Entries.Count > 10000) throw new InvalidDataException("ZIP excede 10.000 arquivos.");
                        long total = 0;
                        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
                        {
                            total += entry.Length;
                            if (entry.Length > Safe.XmlLimit || total > 512L * 1024 * 1024) throw new InvalidDataException("ZIP excede os limites de XML/importação (512 MB).");
                            try { using (var data = entry.Open()) { ImportOne(root, Safe.Read(data, Safe.XmlLimit)); count++; } }
                            catch (InvalidDataException ex) { log(entry.Name + " • " + ex.Message); }
                        }
                    }
                }
                else
                {
                    try { ImportOne(root, Safe.ReadFile(input, Safe.XmlLimit)); count++; }
                    catch (InvalidDataException ex) { log(Path.GetFileName(input) + " • " + ex.Message); }
                }
            }
            return count;
        }
        static void ImportOne(string root, byte[] bytes)
        {
            var doc = LibraryDocument.Parse(Safe.Xml(bytes));
            string cnpj = doc.IsEvent ? doc.Key.Substring(6,14) : doc.Issuer;
            string uf = doc.Key.Substring(0,2) == "35" ? "SP" : doc.Key.Substring(0,2) == "43" ? "RS" : "OutrasUF";
            string dir = Path.Combine(root, "Documentos", cnpj, doc.Environment == 1 ? "Producao" : "Homologacao", uf,
                doc.IsEvent ? "Eventos" : "Notas", doc.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture));
            string filename = doc.Key + (doc.IsEvent ? "-" + doc.EventType + "-" + doc.Sequence : "") + ".xml";
            string path = Path.Combine(dir, filename);
            if (File.Exists(path) && Safe.Hash(Safe.ReadFile(path, Safe.XmlLimit)) != Safe.Hash(bytes))
                Safe.Write(Path.Combine(root, "HistoricoImportacao", Safe.Hash(Safe.ReadFile(path, Safe.XmlLimit)) + ".xml"), Safe.ReadFile(path, Safe.XmlLimit));
            Safe.Write(path, bytes);
        }
        static string Cell(object value)
        {
            string s = Convert.ToString(value, Safe.Br) ?? "";
            if (s.Length > 0 && "=+-@\t\r\n".IndexOf(s[0]) >= 0) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        public static void ExportCsv(string file, IEnumerable<LibraryRow> rows)
        {
            var text = new StringBuilder("Chave;Emissao;Numero;Serie;UF;Direcao;Situacao;CNPJ Emitente;Emitente;Destinatario;Valor;Itens;Formato\r\n");
            foreach (var r in rows)
                text.AppendLine(String.Join(";", new object[] { "'" + r.Chave, r.Emissao.ToString("dd/MM/yyyy HH:mm"), r.Numero, r.Serie, r.UF, r.Direcao, r.Situacao,
                    "'" + r.Emitente, r.NomeEmitente, "'" + r.Destinatario, r.Valor, r.Itens, r.Formato }.Select(Cell).ToArray()));
            Safe.Write(file, new UTF8Encoding(true).GetPreamble().Concat(new UTF8Encoding(false).GetBytes(text.ToString())).ToArray());
        }
        public void ExportZip(string file, IList<LibraryRow> rows)
        {
            var selected = new HashSet<string>(rows.Select(r => r.Chave));
            var files = rows.Select(r => r.Document).Concat(Documents.Where(d => d.IsEvent && selected.Contains(d.Key) && rows.Any(r => r.Chave == d.Key && r.Document.Environment == d.Environment)))
                .Where(d => !String.IsNullOrEmpty(d.FilePath)).GroupBy(d => d.Key + "/" + d.IsEvent + "/" + d.EventType + "/" + d.Sequence).Select(g => g.First()).ToList();
            // Exportar para temporário mantém o destino anterior íntegro se houver falha.
            string tmp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write)) using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                    foreach (var d in files)
                    {
                        byte[] bytes = Safe.ReadFile(d.FilePath, Safe.XmlLimit);
                        string name = (d.IsEvent ? "Eventos/" : "Notas/") + Path.GetFileName(d.FilePath);
                        using (var output = zip.CreateEntry(name, CompressionLevel.Optimal).Open()) output.Write(bytes, 0, bytes.Length);
                    }
                if (File.Exists(file)) File.Replace(tmp, file, null); else File.Move(tmp, file);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }
    }
}
