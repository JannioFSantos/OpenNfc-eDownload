using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace OpenNfceDownload
{
    public sealed class RunSettings
    {
        public string Root, Uf, Cnpj;
        public int Environment = 1;
        // 0=emitidas, 1=recebidas, 2=ambas (no RS). SP consulta o emitente do A1.
        public int Direction;
        public DateTime Start, End;
        public string ContextPath { get { return Path.Combine(Root, "Documentos", Cnpj, Environment == 1 ? "Producao" : "Homologacao", Uf); } }
        public void Validate()
        {
            Cnpj = Safe.Cnpj(Cnpj);
            if (Uf != "SP" && Uf != "RS") throw new ArgumentException("Selecione SP ou RS.");
            if (Environment != 1 && Environment != 2) throw new ArgumentException("Ambiente inválido.");
            if (Direction < 0 || Direction > 2 || (Uf == "SP" && Direction != 0)) throw new ArgumentException("Em SP, o serviço disponibiliza as NFC-e emitidas pelo e-CNPJ.");
            if (Start > End) throw new ArgumentException("A data inicial deve ser anterior à data final.");
            Root = Path.GetFullPath(Root);
            Directory.CreateDirectory(Root);
        }
    }
    public sealed class QueueItem
    {
        public string Key, Status, Message;
    }
    /// <summary>Estado fiscal por estabelecimento, ambiente, UF e filtro RS; sem credenciais.</summary>
    public sealed class DownloadState
    {
        readonly string file;
        public readonly List<QueueItem> Items = new List<QueueItem>();
        public long Cursor;
        public DateTime NotBefore = DateTime.MinValue;
        public DownloadState(RunSettings s)
        {
            file = Path.Combine(s.ContextPath, "Controle", "estado-" + s.Direction + ".xml");
            if (!File.Exists(file)) return;
            XElement root = Safe.Xml(Safe.ReadFile(file, Safe.XmlLimit)).Root;
            if (root == null || root.Name != "estado") throw new InvalidDataException("Estado de download inválido. Preserve o arquivo para recuperar o NSU.");
            Cursor = Long((string)root.Attribute("nsu") ?? "0");
            string next = (string)root.Attribute("aguardarAte");
            if (!String.IsNullOrEmpty(next)) NotBefore = Safe.Utc(next);
            foreach (XElement e in root.Elements("chave"))
            {
                string key = (string)e.Attribute("valor");
                if (!Safe.Key(key) || Items.Any(i => i.Key == key)) throw new InvalidDataException("Fila local contém chave inválida ou repetida.");
                Items.Add(new QueueItem { Key = key, Status = (string)e.Attribute("status") ?? "Pendente", Message = e.Value });
            }
        }
        public static long Long(string value)
        {
            long result;
            if (!Int64.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) || result < 0 || result > 999999999999999L)
                throw new InvalidDataException("NSU inválido no retorno/estado.");
            return result;
        }
        public void Add(IEnumerable<string> keys, bool retry)
        {
            foreach (string key in keys)
            {
                var found = Items.FirstOrDefault(i => i.Key == key);
                if (found == null) Items.Add(new QueueItem { Key = key, Status = "Pendente", Message = "" });
                else if (retry) { found.Status = "Pendente"; found.Message = ""; }
            }
            Save();
        }
        public void Save()
        {
            var root = new XElement("estado", new XAttribute("nsu", Cursor), new XAttribute("aguardarAte", Safe.Date(NotBefore.ToUniversalTime())));
            foreach (var item in Items) root.Add(new XElement("chave", new XAttribute("valor", item.Key), new XAttribute("status", item.Status), item.Message ?? ""));
            Safe.Write(file, Safe.Utf8(new XDocument(root)));
        }
    }
    public sealed class ServiceException : IOException
    {
        public readonly string Code;
        public ServiceException(string code, string message) : base(code + ": " + message) { Code = code; }
    }
    /// <summary>Serialização e intervalo entre chamadas. 656 persiste uma pausa mínima de 1h.</summary>
    public sealed class RequestGate
    {
        readonly string file;
        readonly int spacing;
        public RequestGate(RunSettings s, int milliseconds)
        { file = Path.Combine(s.Root, "Controle", "limite-" + s.Uf + "-" + s.Environment + ".xml"); spacing = milliseconds; }
        public void Wait(CancellationToken token)
        {
            DateTime next = DateTime.MinValue, block = DateTime.MinValue;
            Load(out next, out block);
            if (block > DateTime.UtcNow) throw new IOException("Serviço em pausa por consumo indevido até " + block.ToLocalTime().ToString("dd/MM HH:mm") + ".");
            TimeSpan delay = next - DateTime.UtcNow;
            if (delay > TimeSpan.FromSeconds(15)) throw new IOException("Aguarde antes de repetir a consulta ao serviço.");
            while (next > DateTime.UtcNow)
            {
                int milliseconds = Math.Max(1,(int)Math.Ceiling((next-DateTime.UtcNow).TotalMilliseconds));
                if (token.WaitHandle.WaitOne(milliseconds)) token.ThrowIfCancellationRequested();
            }
            token.ThrowIfCancellationRequested();
            Save(DateTime.UtcNow.AddMilliseconds(spacing), block);
        }
        void Load(out DateTime next, out DateTime block)
        {
            next = block = DateTime.MinValue;
            if (!File.Exists(file)) return;
            XElement e = Safe.Xml(Safe.ReadFile(file, 4096)).Root;
            next = Safe.Utc((string)e.Attribute("proxima")); block = Safe.Utc((string)e.Attribute("bloqueio"));
        }
        void Save(DateTime next, DateTime block)
        { Safe.Write(file, Safe.Utf8(new XDocument(new XElement("limite", new XAttribute("proxima", Safe.Date(next)), new XAttribute("bloqueio", Safe.Date(block)))))); }
        public void Block() { Save(DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(1)); }
    }
    public sealed class DownloadEngine
    {
        static readonly XNamespace N = Safe.Ns;
        readonly RunSettings s;
        readonly ISoapTransport transport;
        readonly Action<string> log;
        readonly RequestGate gate;
        readonly string rsPauseFile;
        public readonly DownloadState State;
        public int SavedCount { get; private set; }
        public DownloadEngine(RunSettings settings, ISoapTransport transport, Action<string> log, int spacing = 6000)
        {
            s = settings; s.Validate(); this.transport = transport; this.log = log ?? delegate { };
            State = new DownloadState(s); gate = new RequestGate(s, spacing);
            rsPauseFile = Path.Combine(s.ContextPath,"Controle","pausa-distribuicao-rs.xml");
        }
        public void CheckKey(string key)
        {
            if (!Safe.Key(key)) throw new InvalidDataException("Chave inválida: use NFC-e modelo 65 com 44 posições e dígito correto.");
            if (s.Uf == "SP" && (key.Substring(0,2) != "35" || key.Substring(6,14) != s.Cnpj))
                throw new InvalidDataException("SP: a chave deve ser paulista e do CNPJ completo do e-CNPJ.");
            if (s.Uf == "RS" && s.Direction == 0 && (key.Substring(0,2) != "43" || key.Substring(6,14) != s.Cnpj))
                throw new InvalidDataException("RS / emitidas: a chave deve ser do RS e do CNPJ consultado. Para destinatário, selecione Recebidas/Ambas.");
        }
        XElement Call(XElement request, string expected, string operation, bool list, CancellationToken token)
        {
            gate.Wait(token);
            byte[] raw = transport.Call(SoapTransport.Endpoint(s.Uf, s.Environment, list), operation, request, s.Uf == "SP" ? "35" : "43", token);
            string name = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" + operation + "-" + Guid.NewGuid().ToString("N").Substring(0,8) + ".xml";
            // Persistência precede parse e atualização do cursor. Nunca registra PFX/senha.
            Safe.Write(Path.Combine(s.ContextPath, "Retornos", name), raw);
            XElement response = SoapResult.Payload(raw, expected);
            if (Safe.Value(response, "tpAmb") != s.Environment.ToString(CultureInfo.InvariantCulture)) throw new InvalidDataException("Ambiente do retorno difere do ambiente solicitado.");
            string code = Safe.Value(response, "cStat");
            if (code == "656") { gate.Block(); throw new ServiceException(code, Safe.Value(response, "xMotivo") + ". Consulta pausada por pelo menos 1 hora."); }
            log(operation + " • " + code + " • " + Safe.Value(response, "xMotivo"));
            return response;
        }
        static XElement Request(string name, int env, params object[] content)
        { return new XElement(N + name, new XAttribute("versao", "1.00"), new XElement(N + "tpAmb", env), content); }
        public static XElement SpListRequest(int env, DateTime start, DateTime end)
        {
            return Request("nfceListagemChaves", env, new XElement(N + "dataHoraInicial", start.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture)),
                new XElement(N + "dataHoraFinal", end.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture)));
        }
        public static XElement SpKeyRequest(int env, string key)
        { return Request("nfceDownloadXML", env, new XElement(N + "chNFCe", key)); }
        public static XElement RsRequest(RunSettings s, long nsu, string key)
        {
            return Request("distNFeRS", s.Environment, new XElement(N + "verAplic", "openNfceDownload1.0"), new XElement(N + "cUF", "43"),
                new XElement(N + "CNPJ", s.Cnpj), new XElement(N + "mod", "65"), key == null ?
                new XElement(N + "solRel", new XElement(N + "indXML", "1"), new XElement(N + "indEmit", s.Direction == 1 ? "0" : "1"),
                    new XElement(N + "indDest", s.Direction == 0 ? "0" : "1"), new XElement(N + "ultNSU", nsu)) :
                new XElement(N + "solDFe", new XElement(N + "chAcesso", key)));
        }
        public void Sync(CancellationToken token)
        {
            if (s.Uf == "SP") SyncSp(token); else SyncRs(token);
        }
        void SyncSp(CancellationToken token)
        {
            var intervals = new Stack<Tuple<DateTime,DateTime>>();
            intervals.Push(Tuple.Create(s.Start, s.End));
            int incomplete = 0;
            while (intervals.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var range = intervals.Pop();
                log("SP • listando " + range.Item1.ToString("dd/MM/yyyy HH:mm") + " até " + range.Item2.ToString("dd/MM/yyyy HH:mm"));
                XElement response = Call(SpListRequest(s.Environment, range.Item1, range.Item2), "retNfceListagemChaves", "nfceListagemChaves", true, token);
                string code = Safe.Value(response, "cStat");
                if (code != "100" && code != "101" && code != "107") throw new ServiceException(code, Safe.Value(response, "xMotivo"));
                var keys = response.Elements(N + "chNFCe").Select(e => e.Value).ToList();
                if (keys.Count > 2000 || (code == "107" && keys.Count > 0)) throw new InvalidDataException("Quantidade de chaves incompatível com o retorno SP.");
                foreach (string key in keys) CheckKey(key);
                State.Add(keys, false);
                if (code == "101")
                {
                    long minutes = (long)(range.Item2 - range.Item1).TotalMinutes;
                    if (minutes == 0)
                    {
                        incomplete++; log("ATENÇÃO: mais de 2.000 chaves em um minuto. Intervalo incompleto: " + range.Item1.ToString("s")); continue;
                    }
                    if (minutes == 1)
                    {
                        intervals.Push(Tuple.Create(range.Item2, range.Item2)); intervals.Push(Tuple.Create(range.Item1, range.Item1));
                    }
                    else
                    {
                        DateTime middle = range.Item1.AddMinutes(minutes / 2);
                        intervals.Push(Tuple.Create(middle, range.Item2)); intervals.Push(Tuple.Create(range.Item1, middle));
                    }
                }
            }
            DownloadQueue(token);
            if (incomplete > 0) throw new IOException("Os XMLs das chaves encontradas foram processados, mas " + incomplete + " minuto(s) excederam o limite de 2.000 chaves. A relação do período está incompleta; confira a Atividade.");
        }
        void RsIdentity(XElement response)
        {
            string cnpj = Safe.Value(response, "CNPJ"), model = Safe.Value(response, "mod");
            if ((cnpj != "" && Safe.Cnpj(cnpj) != s.Cnpj) || (model != "" && model != "65")) throw new InvalidDataException("CNPJ/modelo do lote não correspondem à consulta.");
        }
        List<Tuple<long,string,XElement>> RsDocuments(XElement response)
        {
            RsIdentity(response);
            string compressed = Safe.Value(response, "loteDistComp");
            if (compressed == "") return new List<Tuple<long,string,XElement>>();
            byte[] bytes = Safe.Unzip(compressed);
            XDocument lot = Safe.Xml(bytes);
            if (lot.Root == null || lot.Root.Name != N + "loteDistNFeRS") throw new InvalidDataException("Lote RS descompactado sem estrutura esperada.");
            var result = new List<Tuple<long,string,XElement>>();
            foreach (var proc in lot.Root.Elements(N + "proc"))
            {
                long nsu = DownloadState.Long((string)proc.Attribute("NSU"));
                string key = (string)proc.Attribute("chAcesso");
                CheckKey(key);
                var documents = proc.Elements().ToList();
                if (documents.Count != 1) throw new InvalidDataException("O lote RS contém resumo ou documento sem XML completo. O NSU não foi avançado.");
                result.Add(Tuple.Create(nsu, key, documents[0]));
            }
            if (result.Count > 50 || result.Count != lot.Root.Elements().Count()) throw new InvalidDataException("Lote RS maior que 50 ou com estrutura desconhecida.");
            return result;
        }
        void SaveDocument(XElement element, string key)
        {
            LibraryDocument doc = LibraryDocument.Parse(new XDocument(new XElement(element)));
            if (doc.Key != key || doc.Environment != s.Environment) throw new InvalidDataException("XML recebido com chave/ambiente diferente da consulta.");
            if (!doc.IsEvent)
            {
                if (s.Uf == "SP" && doc.Issuer != s.Cnpj) throw new InvalidDataException("Emitente do XML difere do A1 SP.");
                if (s.Uf == "RS" && !((s.Direction != 1 && doc.Issuer == s.Cnpj) || (s.Direction != 0 && doc.Recipient == s.Cnpj)))
                    throw new InvalidDataException("XML RS fora do filtro emitente/destinatário consultado.");
            }
            string folder = Path.Combine(s.ContextPath, doc.IsEvent ? "Eventos" : "Notas", doc.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture));
            byte[] bytes = Safe.Utf8(new XDocument(new XElement(element)));
            string filename = key + (doc.IsEvent ? "-" + doc.EventType + "-" + doc.Sequence : "") + ".xml";
            string path = Path.Combine(folder, filename);
            // Mudança de invólucro/protocolo não apaga a versão anterior.
            if (File.Exists(path) && Safe.Hash(Safe.ReadFile(path, Safe.XmlLimit)) != Safe.Hash(bytes))
                Safe.Write(Path.Combine(s.ContextPath, "Historico", Safe.Hash(Safe.ReadFile(path, Safe.XmlLimit)) + ".xml"), Safe.ReadFile(path, Safe.XmlLimit));
            Safe.Write(path, bytes); SavedCount++;
        }
        void SyncRs(CancellationToken token)
        {
            CheckRsPause();
            if (State.NotBefore > DateTime.UtcNow) throw new IOException("RS: aguarde até " + State.NotBefore.ToLocalTime().ToString("dd/MM HH:mm") + " para nova distribuição. A biblioteca local continua disponível.");
            for (int page = 0; page < 10000; page++)
            {
                token.ThrowIfCancellationRequested();
                log("RS • distribuindo modelo 65 após NSU " + State.Cursor);
                XElement response = Call(RsRequest(s, State.Cursor, null), "retDistNFeRS", "nfeIntegracaoContab", false, token);
                string code = Safe.Value(response, "cStat");
                if (code != "117" && code != "118") throw new ServiceException(code, Safe.Value(response, "xMotivo"));
                var documents = RsDocuments(response);
                if (code == "117" && documents.Count != 0) throw new InvalidDataException("Retorno RS sem documentos contém XMLs inesperados.");
                long next = Safe.Value(response, "ultNSURet") == "" ? State.Cursor : DownloadState.Long(Safe.Value(response, "ultNSURet"));
                if (next < State.Cursor) throw new InvalidDataException("NSU regressivo: estado local preservado.");
                long prior = State.Cursor;
                foreach (var item in documents)
                {
                    if (item.Item1 <= prior || item.Item1 > next) throw new InvalidDataException("Lote RS fora de ordem ou NSU fora dos limites. Cursor preservado.");
                    prior = item.Item1;
                    SaveDocument(item.Item3, item.Item2);
                }
                if (code == "118" && (documents.Count == 0 || next <= State.Cursor)) throw new InvalidDataException("RS informou documentos, mas não forneceu lote completo/avanço de NSU.");
                // Só confirmar o cursor depois que TODOS os arquivos tiverem sido gravados.
                State.Cursor = next;
                if (code == "117" || documents.Count < 50)
                {
                    State.NotBefore = DateTime.UtcNow.AddMinutes(15);
                    Safe.Write(rsPauseFile,Safe.Utf8(new XDocument(new XElement("pausa",new XAttribute("ate",Safe.Date(State.NotBefore))))));
                }
                State.Save();
                log(documents.Count + " documento(s) salvos • NSU " + State.Cursor + ". Datas selecionadas filtram a biblioteca; o RS distribui a janela disponível por NSU.");
                if (code == "117" || documents.Count < 50) return;
            }
            throw new IOException("Limite de lotes desta execução atingido. O cursor foi salvo; continue na próxima consulta.");
        }
        public void Enqueue(IEnumerable<string> keys, bool retry)
        {
            var all = keys.ToList(); foreach (string key in all) CheckKey(key);
            State.Add(all, retry);
        }
        public void DownloadQueue(CancellationToken token)
        {
            if (s.Uf == "RS" && State.Items.Any(i => i.Status == "Pendente")) CheckRsPause();
            foreach (var item in State.Items.Where(i => i.Status == "Pendente").ToList())
            {
                token.ThrowIfCancellationRequested(); CheckKey(item.Key);
                log("Baixando chave " + item.Key);
                try
                {
                    if (s.Uf == "SP")
                    {
                        XElement response = Call(SpKeyRequest(s.Environment, item.Key), "retNfceDownloadXML", "nfceDownloadXML", false, token);
                        string code = Safe.Value(response, "cStat");
                        if (code != "200") throw new ServiceException(code, Safe.Value(response, "xMotivo"));
                        XElement proc = Safe.Element(response, "proc");
                        var docs = proc == null ? new List<XElement>() : proc.Elements().ToList();
                        if (docs.Count == 0) throw new InvalidDataException("SP devolveu sucesso sem XML. A chave continua pendente.");
                        bool invoice = false;
                        foreach (var e in docs) { SaveDocument(e, item.Key); if (e.Name == N + "nfeProc") invoice = true; }
                        if (!invoice) throw new InvalidDataException("Foram recebidos eventos sem a NFC-e completa. A chave continua pendente.");
                    }
                    else
                    {
                        XElement response = Call(RsRequest(s, 0, item.Key), "retDistNFeRS", "nfeIntegracaoContab", false, token);
                        string code = Safe.Value(response, "cStat");
                        if (code != "118") throw new ServiceException(code, Safe.Value(response, "xMotivo"));
                        var docs = RsDocuments(response);
                        if (docs.Count == 0) throw new InvalidDataException("RS devolveu sucesso sem XML completo. A chave continua pendente.");
                        bool invoice = false;
                        foreach (var d in docs)
                        {
                            if (d.Item2 != item.Key) throw new InvalidDataException("RS devolveu outra chave.");
                            SaveDocument(d.Item3, d.Item2); if (d.Item3.Name == N + "nfeProc") invoice = true;
                        }
                        if (!invoice) throw new InvalidDataException("RS devolveu somente eventos; NFC-e completa ainda pendente.");
                    }
                    item.Status = "Baixado"; item.Message = "XML completo salvo em Notas."; State.Save();
                }
                catch (ServiceException ex)
                {
                    // Erros globais (certificado, autorização, schema, indisponibilidade)
                    // interrompem a fila em vez de repetir o mesmo problema por chave.
                    if (!new[] {"203","204","205","207","217","236","614","615","616","617","618","619","632","8004","117"}.Contains(ex.Code)) throw;
                    item.Status = "Erro"; item.Message = ex.Message; State.Save(); log("Chave sem download • " + ex.Message);
                }
            }
            int errors = State.Items.Count(i => i.Status == "Erro");
            log("Fila: " + State.Items.Count(i => i.Status == "Baixado") + " baixada(s), " + State.Items.Count(i => i.Status == "Pendente") + " pendente(s), " + errors + " com retorno de erro.");
        }
        void CheckRsPause()
        {
            DateTime until = State.NotBefore;
            if (File.Exists(rsPauseFile))
            {
                XElement pause = Safe.Xml(Safe.ReadFile(rsPauseFile,4096)).Root;
                DateTime shared = Safe.Utc((string)pause.Attribute("ate")); if (shared > until) until = shared;
            }
            if (until > DateTime.UtcNow) throw new IOException("RS: aguarde até " + until.ToLocalTime().ToString("dd/MM HH:mm") + " para nova consulta deste CNPJ/modelo 65. A biblioteca local continua disponível.");
        }
    }
}
