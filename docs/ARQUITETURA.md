# Arquitetura e contratos

## Plataforma

Windows Forms, .NET Framework 4.8, C# 5, sem NuGet e sem banco de dados externo. Compilação com `build.ps1`. A aplicação não incorpora componentes de terceiros para consultar documentos.

| Arquivo | Responsabilidade |
| --- | --- |
| `src/Support.cs` | CNPJ numérico/alfanumérico, chave 44 modelo 65, XML limitado sem DTD, GZip, gravação atômica, A1 e identificação SAN ICP-Brasil. |
| `src/Soap.cs` | Endpoints fixos, TLS 1.2/mTLS, WSDL autenticado, seleção de binding/action, construção de envelope e extração do retorno. |
| `src/Downloads.cs` | Contratos fiscais SP/RS, fila, divisão de intervalos, NSU, gravação dos documentos e pausas persistentes. |
| `src/Library.cs` | Leitura de notas/produtos/eventos, filtros, cancelamentos, importação e exportação. |
| `src/MainForm.cs` | Cinco abas, certificado em sessão e preferências de uso separadas dos pacotes. |
| `src/SelfTests.cs` | Fixtures sintéticas, contratos, persistência, mTLS em localhost e teste da interface. |

## SP

Serviços `NFCeListagemChaves.asmx` e `NFCeDownloadXML.asmx` nos hosts `nfce.fazenda.sp.gov.br` e `homologacao.nfce.fazenda.sp.gov.br`.

Payloads versão `1.00`, namespace `http://www.portalfiscal.inf.br/nfe`, sem prefixo no payload, sem whitespace de edição. Datas de listagem têm precisão de minuto. Chaves retornadas por status 101 são preservadas antes da subdivisão. Há sobreposição de fronteiras com deduplicação; subdivisão de intervalo de um minuto produz duas consultas pontuais. Status 101 numa consulta pontual gera aviso de cobertura incompleta, nunca sucesso integral.

No retorno 200, a aplicação extrai `proc/nfeProc` e `proc/procEventoNFe`. Preserva o invólucro simplificado de SP com `dhInc` e `nProt`; não converte esses campos em `protNFe` ou `retEvento` inventados. O documento precisa conter produtos para a fila ser marcada como baixada.

## RS

Endpoint de produção: `https://dfe-servico.svrs.rs.gov.br/WS/NFeIntegracao/NFeIntegracao.asmx`.

Endpoint de homologação: `https://dfe-servico-homologacao.svrs.rs.gov.br/WS/NFeIntegracao/NFeIntegracao.asmx`.

Operação documentada: `nfeIntegracaoContab`. Payload `distNFeRS` versão `1.00`, `cUF=43`, `mod=65`. Distribuição `solRel` com `indXML=1` e indicadores de emitente/destinatário 1/0 conforme direção. Consulta específica usa `solDFe/chAcesso`, sem atualizar cursor da distribuição.

Lotes 118 contêm `loteDistComp` em base64/GZip. Após descompactar, lê `loteDistNFeRS/proc`, com atributos `NSU`, `chAcesso` e conteúdo fiscal. Confere ordem, chave, ambiente, modelo, contexto da consulta, XML completo e limite de 50 documentos. Somente após gravar todos os arquivos confirma `ultNSURet`. Rejeição, XML inválido ou falha de gravação não consomem o cursor do lote.

O estado separa direção, CNPJ, UF e ambiente. A pausa de 15 minutos ao fim da distribuição é compartilhada entre direções do mesmo estabelecimento/modelo. Status 656 cria pausa de uma hora compartilhada pelo estado/ambiente na pasta escolhida. Consultas são sequenciais, com intervalo padrão de seis segundos.

O contrato foi implementado a partir do boletim e dos schemas oficiais disponibilizados no pacote RS, com o endpoint atual publicado pela Receita. A compatibilidade autenticada com o serviço real precisa ser verificada com um contabilista autorizado. A suíte cobre os contratos locais e comunicação mTLS sintética; ela não certifica disponibilidade nem autorização em produção.

## SOAP e WSDL

O aplicativo obtém `?WSDL` com o A1 carregado antes da primeira chamada a cada serviço. O binding determina SOAP 1.2 (preferido) ou 1.1, action, elemento de entrada e eventual cabeçalho. São suportados document/literal com parâmetro XML (`any`/`anyType`) ou string, direto ou envelopado. Não são testadas várias actions contra o serviço.

Imports XSD são buscados apenas por HTTPS no mesmo host, até cinco imports. O endereço publicado no WSDL não substitui o endpoint fixo. Redirecionamentos HTTP não são seguidos. Contrato ausente, ambíguo ou estrutura não suportada interrompe a consulta e mostra erro, em vez de afirmar que não há documentos.

## Persistência e dados

Preferências: `%LOCALAPPDATA%\openNfc-eDownload\preferencias.xml`, contendo somente pasta, CNPJ, estado, ambiente e direção. PFX, caminho do A1 e senha não são persistidos nessas preferências. Os arquivos fiscais e estados ficam na pasta escolhida pelo usuário, fora do executável.

Respostas originais são salvas antes do parse. Arquivos fiscais individuais extraídos são serializados novamente; não se promete identidade byte a byte entre o fragmento SOAP e a extração. O conteúdo de assinaturas é mantido no XML; a aplicação não faz validação criptográfica XMLDSig. Para documentação fiscal original, mantenha também os retornos recebidos.

XMLs alterados são arquivados pelo SHA-256 antes de substituição. Fila e cursor são gravados por arquivo temporário, flush e replace. O aplicativo permite uma instância interativa por sessão do Windows; compartilhar a mesma pasta entre computadores ou instâncias externas exige coordenação adicional de concorrência.

## Limites de validação

XML/resposta/descompactação: 20 MB; importação ZIP: até 10.000 entradas e 512 MB descompactados declarados. Caminhos de entradas ZIP não são extraídos. CSV protege células que iniciem fórmulas e mantém chaves como texto. Erros globais de certificado, autorização e schema interrompem a fila. Rejeições específicas da chave permanecem identificadas para tentativa posterior.

As referências oficiais e o manual estão no README. A pasta local `reference/` é apenas pesquisa; não integra os pacotes. O empacotador usa uma lista explícita e não inclui certificados, dados fiscais, preferências ou diretórios de pesquisa.
