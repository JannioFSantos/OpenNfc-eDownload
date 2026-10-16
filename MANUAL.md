# Manual — openNfc-eDownload 1.0.0

## 1. Preparar o programa

Extraia o ZIP portátil para uma pasta do computador e abra `openNfc-eDownload.exe`. O arquivo `.config` e este manual devem acompanhar o executável. É necessário Windows com .NET Framework 4.8 e acesso à internet para as consultas.

O aplicativo trabalha com **NFC-e, modelo 65**. A chave tem **44 posições**. NF-e modelo 55 e NFS-e usam outros serviços.

## 2. Carregar o certificado A1

Na aba **1. Conexão A1**:

1. Escolha **SP** ou **RS**.
2. Escolha **Produção** para notas reais; **Homologação** é o ambiente de testes da SEFAZ.
3. Selecione a pasta onde os documentos serão salvos.
4. Clique em **Selecionar A1…**, escolha o PFX/P12 e informe a senha.
5. Clique em **Carregar certificado**. A senha e o caminho são removidos dos campos.
6. Confira o CNPJ consultado. Ao carregar um e-CNPJ, o campo é preenchido com o CNPJ do A1.

O certificado não é carregado automaticamente na próxima execução. Para consultar outra empresa com outro certificado, carregue o novo A1. **Liberar certificado da sessão** encerra o uso do A1 atual.

## 3. Consultar São Paulo

O serviço paulista usa o **e-CNPJ do estabelecimento emitente**. O CNPJ completo deve ser idêntico ao do certificado, inclusive filial. Ele fornece as NFC-e emitidas por esse estabelecimento; a opção **Consultar** fica em **Emitidas**.

Na aba **2. Consultar período**, escolha data e hora iniciais e finais e clique em **Consultar e baixar**. O programa consulta as chaves e baixa os XMLs em sequência. O horário final futuro é limitado ao momento da consulta.

A disponibilidade inicialmente documentada é de **100 dias**, tanto para listar quanto para baixar. Quando houver mais de 2.000 chaves no intervalo, o aplicativo divide o período e remove duplicatas. Se até um único minuto exceder o limite, o programa informa que a relação está incompleta e preserva as chaves encontradas.

O retorno SP usa um invólucro com protocolo simplificado. Ele contém a NFC-e com os produtos, mas seu grupo de protocolo difere do `nfeProc` padrão. A coluna **Formato** identifica esse caso. O aplicativo mantém esse formato e não fabrica um protocolo diferente.

## 4. Consultar Rio Grande do Sul

O serviço RS é destinado à integração de contabilistas. Use o **e-CPF ou e-CNPJ A1 do contabilista**, com vínculo e autorização eletrônica do estabelecimento no e-CAC. A autorização indicada na documentação é **Nota Fiscal Eletrônica: Autoriza Distribuição para Contabilista**.

Informe o CNPJ do cliente no campo **CNPJ consultado**. Na opção **Consultar**, escolha **Emitidas**, **Recebidas** ou **Ambas**. A autorização e os documentos acessíveis são verificados pela SEFAZ.

Clique em **Consultar e baixar**. O RS entrega os documentos em lotes, por **NSU**, e o aplicativo solicita o XML completo do modelo 65. Todos os documentos entregues pelo serviço são salvos, mesmo quando a data deles estiver fora do filtro escolhido.

**O serviço não recebe uma data inicial/final.** As datas selecionadas são aplicadas à biblioteca depois da coleta. O período disponível documentado corresponde ao mês corrente e aos dois anteriores, a partir do primeiro dia de dois meses atrás. Por exemplo, numa consulta em outubro, a janela começa em 1º de agosto.

O NSU é salvo por CNPJ, ambiente e direção. Mantenha a mesma pasta de dados para continuar a coleta. Ao terminar a distribuição, o programa aguarda **15 minutos** antes de novas consultas desse CNPJ/modelo. Trocar o filtro não elimina essa espera.

## 5. Baixar chaves copiadas de planilhas

Na aba **3. Chaves em lote**:

1. Copie as células com as chaves completas, sem o cabeçalho da coluna.
2. Cole no campo. São aceitas linhas, tabulações, espaços, vírgulas e ponto e vírgula.
3. Confira a contagem de chaves válidas e entradas inválidas.
4. Clique em **Baixar chaves coladas**.

As duplicatas são removidas. Se a planilha transformar a chave em notação científica, recupere a chave completa antes de colar: os dígitos perdidos não podem ser reconstruídos.

Em SP, as chaves precisam ser do estado e do CNPJ exato do A1. No RS, a consulta depende do vínculo do cliente com a nota e da autorização do contabilista.

As chaves já baixadas são aproveitadas da fila. Marque **Consultar novamente as chaves coladas, inclusive já baixadas** somente quando precisar refazer a consulta. Essa ação também permite obter eventos novos, quando disponíveis.

Colar a chave não garante a disponibilidade de uma nota antiga. O aplicativo não recebe XML completo quando o serviço recusa o download por prazo ou permissão.

## 6. Entender a fila

| Situação | Significado |
| --- | --- |
| Pendente | A chave ainda precisa ser processada, inclusive após interrupção da conexão. |
| Baixado | O XML com produtos foi gravado em `Notas`. |
| Erro | O serviço devolveu uma rejeição específica da chave; veja a coluna Retorno. |

**Retomar fila pendente** continua as chaves pendentes. **Tentar novamente os erros** recoloca os erros na fila. Não há repetição automática infinita de rejeições.

**Parar** interrompe a execução preservando o progresso gravado. No RS, um lote parcialmente salvo não confirma o NSU; os documentos são retomados na consulta seguinte. Aguarde o término antes de fechar o aplicativo.

## 7. Localizar XMLs e eventos

A pasta padrão é `openNfc-eDownload-dados` dentro da pasta do usuário. Dentro da pasta escolhida:

```text
Documentos/
  CNPJ/
    Producao/ ou Homologacao/
      SP/ ou RS/
        Notas/AAAA-MM/       XMLs das NFC-e com produtos
        Eventos/AAAA-MM/     Eventos recebidos, como cancelamento
        Retornos/           Respostas originais dos serviços
        Controle/           Fila, NSU e pausas
        Historico/          Versões anteriores de XMLs substituídos
Controle/                   Intervalos e pausas dos serviços
```

Os XMLs extraídos de uma resposta SOAP são serializados em arquivos individuais. A resposta original é mantida em `Retornos`. XMLs importados são preservados como recebidos na importação.

## 8. Usar filtros e analisar produtos

Na aba **4. Biblioteca e análise**, escolha período, UF do emitente, direção e situação. A busca encontra chave, número, série, emitente, destinatário e descrição de produto. Clique em **Filtrar**.

Selecione uma linha para visualizar produtos, NCM, CFOP, unidade, quantidade, valor unitário e total. O resumo mostra o total sem as notas canceladas, os cancelamentos separados e a quantidade de itens.

Uma nota é marcada como cancelada quando há evento de cancelamento registrado entre os arquivos locais. Uma nota cujo cancelamento ainda não foi recebido pode continuar aparecendo sem essa marcação. Os valores representam os documentos carregados na biblioteca.

**Abrir XML selecionado** abre o arquivo; **Abrir pasta do XML** localiza a pasta no computador. **Recarregar** relê os arquivos locais. Produção e homologação são exibidas separadamente conforme a conexão selecionada.

## 9. Exportar ou importar

- **Exportar filtradas (ZIP)**: inclui os XMLs das notas visíveis e os eventos associados disponíveis.
- **Exportar análise (CSV)**: salva a relação filtrada com valores, situação e identificação das notas. As chaves são exportadas como texto.
- **Importar XML / ZIP…**: adiciona documentos existentes à biblioteca. Os arquivos devem ser de NFC-e modelo 65; resumos sem produtos não são tratados como notas completas.

Os filtros atuam na exibição e exportação; não apagam os documentos da pasta.

## 10. Consultar a Atividade

A aba **5. Atividade** mostra as etapas e os retornos oficiais. Alguns exemplos:

| Retorno | Procedimento |
| --- | --- |
| SP 104 / 207; RS 632 | Período fora da disponibilidade do serviço; utilize XML já arquivado ou obtido do emitente. |
| RS 8002 | Confira o vínculo do contabilista com o CNPJ consultado. |
| RS 8003 | Confira a autorização eletrônica no e-CAC. |
| SP 203 | Carregue o e-CNPJ do estabelecimento da chave. |
| 656 | Aguarde a pausa exibida, de pelo menos uma hora. O programa não insiste no serviço durante a pausa. |
| Falha TLS / HTTP 403 | Confira validade do A1, relógio do Windows, cadeia ICP-Brasil e permissão para o serviço. |
| XML completo ausente | A chave permanece pendente; sucesso sem XML não é marcado como download concluído. |

Para a diferença entre os serviços e os links das referências oficiais, consulte o `README.md`.
