using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace OpenNfceDownload
{
    /// <summary>Interface sem dependências externas. As consultas usam uma cópia dos campos
    /// para que mudar filtros da biblioteca não altere o lote em processamento.</summary>
    public sealed class MainForm : Form
    {
        readonly string preferences;
        readonly bool testing;
        CertificateSession certificate;
        CancellationTokenSource cancellation;
        bool busy;
        readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill };
        readonly ComboBox uf = Combo("SP", "RS"), environment = Combo("Produção", "Homologação"), direction = Combo("Emitidas", "Recebidas", "Ambas");
        readonly TextBox cnpj = new TextBox(), root = new TextBox(), pfx = new TextBox(), password = new TextBox { UseSystemPasswordChar = true };
        readonly Label certInfo = TextLabel("Nenhum certificado carregado. Selecione um A1 PFX/P12."), serviceInfo = TextLabel(""), periodInfo = TextLabel("");
        readonly DateTimePicker start = DatePicker(), end = DatePicker();
        readonly TextBox keys = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, AcceptsReturn = true, AcceptsTab = true, Dock = DockStyle.Fill, MaxLength = 2000000, Font = new Font("Consolas", 10) };
        readonly Label keyCount = TextLabel("Cole as chaves de 44 posições, uma por linha ou copiadas de uma coluna da planilha.");
        readonly CheckBox retry = new CheckBox { Text = "Consultar novamente as chaves coladas, inclusive já baixadas", AutoSize = true, Checked = false };
        readonly TextBox activity = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, MaxLength = 2000000, Font = new Font("Consolas", 9) };
        readonly DataGridView queue = Grid(), invoices = Grid(), products = Grid();
        readonly DateTimePicker filterStart = DatePicker(), filterEnd = DatePicker();
        readonly ComboBox filterUf = Combo("Todas", "SP", "RS"), filterDirection = Combo("Todas", "Emitidas", "Recebidas"), filterStatus = Combo("Todas", "Sem cancelamento", "Canceladas");
        readonly TextBox search = new TextBox { Width = 240 };
        readonly Label analysis = TextLabel("Nenhum XML na biblioteca."), footer = TextLabel("Pronto."), selectedInfo = TextLabel("Selecione uma NFC-e para ver os produtos.");
        readonly List<Button> runButtons = new List<Button>();
        readonly Button cancel = new Button { Text = "Parar", AutoSize = true, Enabled = false };
        readonly DocumentLibrary library = new DocumentLibrary();
        List<LibraryRow> visible = new List<LibraryRow>();
        TabPage connection;
        public MainForm(string testRoot)
        {
            testing = testRoot != null;
            preferences = Path.Combine(testRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "openNfc-eDownload", "preferencias.xml");
            Text = "openNfc-eDownload • SP e RS • 1.0.0";
            Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(245,247,250);
            Size = new Size(1220,850); MinimumSize = new Size(1080,740); StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            root.Text = testRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "openNfc-eDownload-dados");
            start.Value = DateTime.Today.AddDays(-7); end.Value = DateTime.Today.AddHours(23).AddMinutes(59);
            filterStart.Value = DateTime.Today.AddMonths(-2).AddDays(1-DateTime.Today.Day); filterEnd.Value = DateTime.Today.AddHours(23).AddMinutes(59);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Color.FromArgb(24,45,72), Padding = new Padding(14,6,14,6) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,75)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));
            var title = TextLabel("openNfc-eDownload"); title.Font = new Font("Segoe UI", 19, FontStyle.Bold); title.ForeColor = Color.White; title.Margin = Padding.Empty;
            var subtitle = TextLabel("NFC-e modelo 65  •  Certificado A1  •  São Paulo e Rio Grande do Sul"); subtitle.ForeColor = Color.FromArgb(215,228,246); subtitle.Margin = Padding.Empty;
            var github = new LinkLabel { Text = "github.com/JannioFSantos", AutoSize = true, LinkColor = Color.FromArgb(157,205,255), Anchor = AnchorStyles.Right };
            github.LinkClicked += delegate { OpenPath("https://github.com/JannioFSantos"); };
            header.Controls.Add(title,0,0); header.Controls.Add(subtitle,0,1); header.Controls.Add(github,1,0); header.SetRowSpan(github,2);
            layout.Controls.Add(header,0,0); layout.Controls.Add(tabs,0,1);
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 }; bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.Dock = DockStyle.Fill; footer.TextAlign = ContentAlignment.MiddleLeft; bottom.Controls.Add(footer,0,0); bottom.Controls.Add(cancel,1,0); layout.Controls.Add(bottom,0,2);
            Controls.Add(layout);
            BuildConnection(); BuildPeriod(); BuildKeys(); BuildLibrary(); BuildActivity();
            cancel.Click += delegate { if (cancellation != null) { cancellation.Cancel(); footer.Text = "Parando após preservar os arquivos recebidos…"; } };
            uf.SelectedIndexChanged += delegate { ProviderChanged(); }; environment.SelectedIndexChanged += delegate { if (!busy) { SavePreferences(); ReloadLibrary(); ReloadQueue(); } };
            direction.SelectedIndexChanged += delegate { if (!busy) ReloadQueue(); };
            keys.TextChanged += delegate { UpdateKeyCount(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (busy) { cancellation.Cancel(); e.Cancel = true; footer.Text = "Aguarde o encerramento da consulta antes de fechar."; return; }
                SavePreferences(); if (certificate != null) certificate.Dispose();
            };
            LoadPreferences(); ProviderChanged();
            Shown += delegate { ReloadLibrary(); ReloadQueue(); };
        }
        static DateTimePicker DatePicker() { return new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy HH:mm", Width = 172 }; }
        static Label TextLabel(string text) { return new Label { Text = text, AutoSize = true, MaximumSize = new Size(1040,0), Margin = new Padding(4,7,4,7) }; }
        static ComboBox Combo(params string[] items)
        { var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 175 }; combo.Items.AddRange(items); combo.SelectedIndex = 0; return combo; }
        static DataGridView Grid()
        {
            return new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoGenerateColumns = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells, RowHeadersVisible = false, ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText };
        }
        TabPage Page(string name)
        { var page = new TabPage(name) { Padding = new Padding(14), BackColor = Color.White }; tabs.TabPages.Add(page); return page; }
        static FlowLayoutPanel Flow(params Control[] controls)
        { var flow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(0), Padding = new Padding(0,4,0,4) }; flow.Controls.AddRange(controls); return flow; }
        Button Button(string label, Action action, bool operation)
        {
            var button = new Button { Text = label, AutoSize = true, Padding = new Padding(8,4,8,4), Margin = new Padding(4) };
            button.Click += delegate { Guard(action); }; if (operation) runButtons.Add(button); return button;
        }
        void Guard(Action action)
        { try { action(); } catch (Exception ex) { Log(ex.Message); MessageBox.Show(this, ex.Message, "openNfc-eDownload", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
        void BuildConnection()
        {
            connection = Page("1. Conexão A1");
            var stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
            stack.Controls.Add(TextLabel("Configure o estado e carregue o certificado para iniciar. Os dados de cada CNPJ ficam separados."));
            stack.Controls.Add(Flow(TextLabel("Estado"), uf, TextLabel("Ambiente"), environment, TextLabel("Consultar"), direction));
            var table = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 3, Padding = new Padding(0,8,0,8) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,170)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            AddField(table,0,"CNPJ consultado", cnpj, null);
            AddField(table,1,"Pasta dos documentos", root, Button("Escolher pasta…", delegate { using (var dialog = new FolderBrowserDialog { SelectedPath = root.Text, Description = "Pasta local dos XMLs e do controle de downloads" }) if (dialog.ShowDialog(this) == DialogResult.OK) { root.Text = dialog.SelectedPath; SavePreferences(); ReloadLibrary(); ReloadQueue(); } },false));
            AddField(table,2,"Certificado A1 (PFX/P12)", pfx, Button("Selecionar A1…", delegate { using (var dialog = new OpenFileDialog { Filter = "Certificado A1|*.pfx;*.p12", CheckFileExists = true }) if (dialog.ShowDialog(this) == DialogResult.OK) pfx.Text = dialog.FileName; },false));
            AddField(table,3,"Senha do A1", password, Button("Carregar certificado", LoadCertificate,false));
            stack.Controls.Add(table); stack.Controls.Add(certInfo);
            stack.Controls.Add(Flow(Button("Liberar certificado da sessão", delegate { if (certificate != null) certificate.Dispose(); certificate = null; password.Clear(); pfx.Clear(); certInfo.Text = "Certificado liberado. Carregue um A1 para consultar."; },false), Button("Abrir pasta de documentos", delegate { Directory.CreateDirectory(root.Text); OpenPath(root.Text); },false), Button("Manual de uso", OpenManual,false)));
            serviceInfo.ForeColor = Color.FromArgb(30,77,124); stack.Controls.Add(serviceInfo);
            stack.Controls.Add(TextLabel("O certificado e a senha ficam somente na sessão. Ao trocar de computador ou encerrar o programa, carregue o A1 novamente."));
            connection.Controls.Add(stack);
            cnpj.Leave += delegate { if (!busy) { SavePreferences(); ReloadLibrary(); ReloadQueue(); } };
        }
        static void AddField(TableLayoutPanel table, int row, string label, Control control, Control extra)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); control.Dock = DockStyle.Fill; control.Margin = new Padding(4,7,4,7);
            table.Controls.Add(TextLabel(label),0,row); table.Controls.Add(control,1,row); if (extra != null) table.Controls.Add(extra,2,row);
        }
        void BuildPeriod()
        {
            var page = Page("2. Consultar período");
            var stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
            var title = TextLabel("Consultar e baixar XMLs completos"); title.Font = new Font(Font, FontStyle.Bold); stack.Controls.Add(title);
            stack.Controls.Add(Flow(TextLabel("De"), start, TextLabel("Até"), end)); stack.Controls.Add(periodInfo);
            stack.Controls.Add(Flow(Button("Consultar e baixar", delegate { StartRun(true, null); },true), Button("Retomar fila pendente", delegate { StartRun(false, null); },true)));
            stack.Controls.Add(TextLabel("SP: a lista é dividida automaticamente quando excede 2.000 chaves. As chaves encontradas entram na fila e são baixadas em sequência."));
            stack.Controls.Add(TextLabel("RS: o NSU é salvo por CNPJ, ambiente e filtro. Todos os XMLs distribuídos são preservados. O período escolhido é aplicado à biblioteca ao fim da consulta."));
            stack.Controls.Add(TextLabel("Não são emitidas notas nem enviados eventos de ciência ou confirmação. Este aplicativo consulta e baixa os documentos disponibilizados pelos serviços estaduais."));
            page.Controls.Add(stack);
        }
        void BuildKeys()
        {
            var page = Page("3. Chaves em lote");
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,42)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,58));
            layout.Controls.Add(keyCount,0,0); layout.Controls.Add(keys,0,1); layout.Controls.Add(retry,0,2);
            layout.Controls.Add(Flow(Button("Baixar chaves coladas", delegate
            {
                List<string> invalid; var valid = Safe.Keys(keys.Text, out invalid);
                if (invalid.Count > 0) throw new InvalidDataException(invalid.Count + " entrada(s) inválida(s). Use a chave completa de 44 posições, modelo 65. Exemplo rejeitado: " + invalid[0]);
                if (valid.Count == 0) throw new InvalidDataException("Cole pelo menos uma chave de NFC-e.");
                StartRun(false, valid);
            },true), Button("Tentar novamente os erros", delegate { StartRun(false, new List<string>()); },true), Button("Limpar campo", delegate { keys.Clear(); },false)),0,3);
            layout.Controls.Add(TextLabel("Fila do CNPJ, ambiente e filtro selecionados. XMLs salvos em Notas; cancelamentos em Eventos."),0,4); layout.Controls.Add(queue,0,5);
            page.Controls.Add(layout);
        }
        void BuildLibrary()
        {
            var page = Page("4. Biblioteca e análise");
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(Flow(TextLabel("De"),filterStart,TextLabel("Até"),filterEnd, TextLabel("UF emitente"),filterUf,TextLabel("Direção"),filterDirection),0,0);
            layout.Controls.Add(Flow(TextLabel("Situação"),filterStatus,TextLabel("Buscar"),search,Button("Filtrar", ApplyFilters,false),Button("Recarregar",ReloadLibrary,false)),0,1);
            analysis.Font = new Font(Font,FontStyle.Bold); layout.Controls.Add(analysis,0,2);
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(1050,430), SplitterDistance = 225, Panel1MinSize = 100, Panel2MinSize = 100 };
            split.Panel1.Controls.Add(invoices);
            var detail = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 }; detail.RowStyles.Add(new RowStyle(SizeType.AutoSize)); detail.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            detail.Controls.Add(selectedInfo,0,0); detail.Controls.Add(products,0,1); split.Panel2.Controls.Add(detail); layout.Controls.Add(split,0,3);
            layout.Controls.Add(Flow(Button("Abrir XML selecionado", delegate { LibraryRow row = Selected(); OpenPath(row.Document.FilePath); },false),
                Button("Abrir pasta do XML", delegate { OpenPath(Path.GetDirectoryName(Selected().Document.FilePath)); },false),
                Button("Exportar filtradas (ZIP)", ExportZip,false),Button("Exportar análise (CSV)", ExportCsv,false),Button("Importar XML / ZIP…",ImportFiles,true)),0,4);
            page.Controls.Add(layout); invoices.SelectionChanged += delegate { ShowProducts(); };
            search.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { ApplyFilters(); e.SuppressKeyPress = true; } };
        }
        void BuildActivity()
        {
            var page = Page("5. Atividade");
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 }; layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layout.Controls.Add(Flow(Button("Copiar atividade", delegate { if (activity.Text.Length > 0) Clipboard.SetText(activity.Text); },false),Button("Limpar atividade", delegate { activity.Clear(); },false)),0,0);
            layout.Controls.Add(activity,0,1); page.Controls.Add(layout);
        }
        void ProviderChanged()
        {
            bool sp = uf.SelectedIndex == 0;
            if (sp) direction.SelectedIndex = 0; direction.Enabled = !sp;
            serviceInfo.Text = sp ? "SP • Use o e-CNPJ A1 do estabelecimento. Os serviços oficiais listam e baixam suas NFC-e emitidas. Janela inicialmente documentada: 100 dias." :
                "RS • Use o A1 e-CPF/e-CNPJ do contabilista autorizado eletronicamente pelo cliente no e-CAC. Informe o CNPJ do estabelecimento. O serviço valida a autorização.";
            periodInfo.Text = sp ? "SP: consulta real por data e hora de emissão. O serviço pode limitar o período retroativo (inicialmente 100 dias)." :
                "RS: distribuição por NSU, sem filtro remoto por data. O período abaixo será usado na biblioteca. Disponibilidade documentada: mês atual e os dois anteriores.";
            if (!busy) { SavePreferences(); ReloadQueue(); }
        }
        void LoadCertificate()
        {
            CertificateSession loaded;
            try { loaded = new CertificateSession(pfx.Text, password.Text); }
            finally { password.Clear(); }
            if (certificate != null) certificate.Dispose(); certificate = loaded;
            if (loaded.Cnpj != "") cnpj.Text = loaded.Cnpj; else cnpj.Clear();
            certInfo.Text = (loaded.Cnpj != "" ? "e-CNPJ " : "e-CPF ") + loaded.Identity + " • válido até " + loaded.Certificate.NotAfter.ToString("dd/MM/yyyy HH:mm");
            pfx.Clear(); Log("Certificado A1 carregado. A senha e o caminho foram removidos dos campos."); SavePreferences(); ReloadLibrary(); ReloadQueue();
        }
        RunSettings Settings(bool requireCert)
        {
            var s = new RunSettings { Root = root.Text, Uf = (string)uf.SelectedItem, Environment = environment.SelectedIndex + 1, Cnpj = cnpj.Text,
                Direction = direction.SelectedIndex, Start = Minute(start.Value), End = Minute(end.Value) };
            s.Validate();
            if (requireCert)
            {
                if (certificate == null) throw new InvalidOperationException("Carregue um certificado A1 na aba Conexão.");
                certificate.Validate(s.Cnpj,s.Uf);
            }
            return s;
        }
        static DateTime Minute(DateTime value) { return new DateTime(value.Year,value.Month,value.Day,value.Hour,value.Minute,0,DateTimeKind.Unspecified); }
        async void StartRun(bool sync, IList<string> input)
        {
            if (busy) return;
            try
            {
                RunSettings s = Settings(true);
                if (sync && s.End > DateTime.Now.AddMinutes(1)) s.End = Minute(DateTime.Now);
                if (s.Start > s.End) throw new InvalidDataException("O início do período está no futuro.");
                var engine = new DownloadEngine(s,new SoapTransport(certificate.Certificate),Log);
                if (input != null)
                {
                    if (input.Count == 0) engine.Enqueue(engine.State.Items.Where(i => i.Status == "Erro").Select(i => i.Key).ToList(),true);
                    else engine.Enqueue(input,retry.Checked);
                }
                SavePreferences(); SetBusy(true); Log("Iniciando " + s.Uf + " • " + s.Cnpj + " • " + (s.Environment == 1 ? "Produção" : "Homologação"));
                var token = cancellation.Token;
                await Task.Run(delegate { if (sync) engine.Sync(token); else engine.DownloadQueue(token); },token);
                if (sync) { filterStart.Value = s.Start; filterEnd.Value = s.End; }
                int errors = engine.State.Items.Count(i => i.Status == "Erro");
                footer.Text = "Consulta encerrada • " + engine.SavedCount + " arquivo(s) salvo(s) nesta execução" + (errors > 0 ? " • " + errors + " chave(s) com erro; confira a fila." : ".");
                Log(footer.Text);
            }
            catch (OperationCanceledException) { footer.Text = "Consulta interrompida. Fila e NSU preservados."; Log(footer.Text); }
            catch (Exception ex)
            {
                footer.Text = "Consulta interrompida: " + ex.Message; Log(footer.Text);
                MessageBox.Show(this,ex.Message,"Consulta não concluída",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
            finally { if (busy) SetBusy(false); ReloadQueue(); ReloadLibrary(); }
        }
        void SetBusy(bool value)
        {
            busy = value; connection.Enabled = !value; cancel.Enabled = value;
            foreach (var button in runButtons) button.Enabled = !value;
            if (value) { cancellation = new CancellationTokenSource(); footer.Text = "Consultando…"; }
            else if (cancellation != null) { cancellation.Dispose(); cancellation = null; }
        }
        public void Log(string text)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log),text); return; }
            if (activity.TextLength > 1000000) activity.Text = activity.Text.Substring(activity.TextLength-500000);
            activity.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine);
        }
        void UpdateKeyCount()
        {
            List<string> invalid; var valid = Safe.Keys(keys.Text,out invalid);
            keyCount.Text = keys.TextLength == 0 ? "Cole as chaves de 44 posições, uma por linha ou de uma coluna da planilha." : valid.Count + " chave(s) válida(s), sem duplicatas • " + invalid.Count + " entrada(s) inválida(s).";
        }
        void ReloadQueue()
        {
            if (busy) return;
            try
            {
                var state = new DownloadState(Settings(false));
                queue.DataSource = state.Items.Select(i => new { Chave = i.Key, Situacao = i.Status, Retorno = i.Message }).ToList();
            }
            catch (Exception ex) { queue.DataSource = null; if (cnpj.Text.Trim() != "") Log("Fila: " + ex.Message); }
        }
        void ReloadLibrary()
        {
            if (busy) return;
            try
            {
                library.Load(Path.GetFullPath(root.Text)); ApplyFilters();
                if (library.Warnings.Count > 0) Log("Biblioteca: " + library.Warnings.Count + " XML(s) não carregado(s). " + library.Warnings[0]);
            }
            catch (Exception ex) { Log("Biblioteca: " + ex.Message); }
        }
        void ApplyFilters()
        {
            string id = "";
            if (!String.IsNullOrWhiteSpace(cnpj.Text)) id = Safe.Cnpj(cnpj.Text);
            visible = library.Filter(id,environment.SelectedIndex+1,(string)filterUf.SelectedItem,filterStart.Value,filterEnd.Value,search.Text,filterDirection.SelectedIndex,filterStatus.SelectedIndex);
            invoices.DataSource = visible;
            foreach (DataGridViewColumn column in invoices.Columns)
            {
                if (column.Name == "Valor") { column.DefaultCellStyle.Format = "N2"; column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; }
                if (column.Name == "Emissao") column.DefaultCellStyle.Format = "dd/MM/yyyy HH:mm";
            }
            int cancelled = visible.Count(r => r.Situacao == "Cancelada");
            analysis.Text = visible.Count + " NFC-e • Total sem canceladas: " + visible.Where(r => r.Situacao != "Cancelada").Sum(r => r.Valor).ToString("C2",Safe.Br) +
                " • Canceladas: " + cancelled + " (" + visible.Where(r => r.Situacao == "Cancelada").Sum(r => r.Valor).ToString("C2",Safe.Br) + ") • " + visible.Sum(r => r.Itens) + " itens";
            ShowProducts();
        }
        LibraryRow Selected()
        { if (invoices.CurrentRow == null || !(invoices.CurrentRow.DataBoundItem is LibraryRow)) throw new InvalidOperationException("Selecione uma NFC-e na biblioteca."); return (LibraryRow)invoices.CurrentRow.DataBoundItem; }
        void ShowProducts()
        {
            if (invoices.CurrentRow == null) { products.DataSource = null; selectedInfo.Text = "Selecione uma NFC-e para ver os produtos."; return; }
            var row = invoices.CurrentRow.DataBoundItem as LibraryRow; if (row == null) return;
            products.DataSource = row.Document.Products; selectedInfo.Text = "NFC-e " + row.Numero + " / série " + row.Serie + " • Protocolo " + (row.Document.Protocol ?? "") + " • " + row.Formato;
            foreach (DataGridViewColumn column in products.Columns)
                if (new[] {"Quantity","UnitPrice","Total"}.Contains(column.Name)) { column.DefaultCellStyle.Format = "N4"; column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; }
            var captions = new Dictionary<string,string> { {"Number","Item"},{"Code","Código"},{"Description","Descrição"},{"Ncm","NCM"},{"Cfop","CFOP"},{"Unit","Unidade"},{"Quantity","Quantidade"},{"UnitPrice","Vlr. unitário"},{"Total","Total"} };
            foreach (var caption in captions) if (products.Columns.Contains(caption.Key)) products.Columns[caption.Key].HeaderText = caption.Value;
        }
        void ExportCsv()
        {
            if (visible.Count == 0) throw new InvalidOperationException("Não há notas no filtro atual.");
            using (var dialog = new SaveFileDialog { Filter = "Análise CSV|*.csv", FileName = "NFCe-analise.csv" })
                if (dialog.ShowDialog(this) == DialogResult.OK) { DocumentLibrary.ExportCsv(dialog.FileName,visible); Log("Análise exportada: " + visible.Count + " notas."); }
        }
        void ExportZip()
        {
            if (visible.Count == 0) throw new InvalidOperationException("Não há notas no filtro atual.");
            using (var dialog = new SaveFileDialog { Filter = "XMLs e eventos ZIP|*.zip", FileName = "NFCe-XMLs.zip" })
                if (dialog.ShowDialog(this) == DialogResult.OK) { library.ExportZip(dialog.FileName,visible); Log("ZIP exportado com notas filtradas e eventos disponíveis."); }
        }
        async void ImportFiles()
        {
            if (busy) return;
            using (var dialog = new OpenFileDialog { Filter = "XML ou ZIP|*.xml;*.zip", Multiselect = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string[] files = dialog.FileNames; string folder = Path.GetFullPath(root.Text);
                try { SetBusy(true); int count = await Task.Run(delegate { return DocumentLibrary.Import(folder,files,Log); }); Log(count + " documento(s) importado(s)."); }
                catch (Exception ex) { Log(ex.Message); MessageBox.Show(this,ex.Message,"Importação",MessageBoxButtons.OK,MessageBoxIcon.Warning); }
                finally { SetBusy(false); ReloadLibrary(); }
            }
        }
        void OpenManual()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"MANUAL.md");
            if (!File.Exists(path)) throw new FileNotFoundException("O MANUAL.md deve estar ao lado do executável.");
            OpenPath(path);
        }
        void OpenPath(string path)
        { if (!testing) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        void LoadPreferences()
        {
            if (!File.Exists(preferences)) return;
            try
            {
                XElement e = Safe.Xml(Safe.ReadFile(preferences,65536)).Root;
                if (e == null || e.Name != "preferencias") return;
                string path = (string)e.Element("pasta"); if (!String.IsNullOrWhiteSpace(path)) root.Text = path;
                cnpj.Text = (string)e.Element("cnpj") ?? "";
                string state = (string)e.Element("uf"); uf.SelectedIndex = state == "RS" ? 1 : 0;
                environment.SelectedIndex = (string)e.Element("ambiente") == "2" ? 1 : 0;
                int dir; if (Int32.TryParse((string)e.Element("direcao"),out dir) && dir >= 0 && dir <= 2) direction.SelectedIndex = dir;
            }
            catch (Exception ex) { Log("Preferências não carregadas: " + ex.Message); }
        }
        void SavePreferences()
        {
            if (testing || busy) return;
            try { Safe.Write(preferences,Safe.Utf8(new XDocument(new XElement("preferencias",new XElement("pasta",root.Text),new XElement("cnpj",cnpj.Text),new XElement("uf",uf.SelectedItem),new XElement("ambiente",environment.SelectedIndex+1),new XElement("direcao",direction.SelectedIndex))))); }
            catch (Exception ex) { Log("Não foi possível salvar as preferências: " + ex.Message); }
        }
        public void SmokeTest()
        {
            foreach (TabPage page in tabs.TabPages) { tabs.SelectedTab = page; PerformLayout(); Application.DoEvents(); if (page.ClientSize.Width < 900) throw new Exception("Aba estreita: " + page.Text); }
            uf.SelectedIndex = 1; direction.SelectedIndex = 2; ProviderChanged(); uf.SelectedIndex = 0;
            if (tabs.TabCount != 5 || direction.SelectedIndex != 0 || direction.Enabled) throw new Exception("Configuração das abas/estados inválida.");
            tabs.SelectedIndex = 0;
        }
    }
}
