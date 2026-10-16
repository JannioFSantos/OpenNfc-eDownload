# openNfc-eDownload

Aplicativo independente para Windows que consulta e baixa XMLs de **NFC-e (modelo 65)** com certificado **A1**, pelos serviços oficiais de **São Paulo e Rio Grande do Sul**.

Projeto: [JannioFSantos](https://github.com/JannioFSantos).

## Baixar

- [Versão 1.0.0 — programa portátil para Windows](https://github.com/JannioFSantos/OpenNfc-eDownload/releases/download/v1.0.0/openNfc-eDownload-1.0.0-portatil.zip).
- [Versão 1.0.0 — código-fonte com scripts de compilação](https://github.com/JannioFSantos/OpenNfc-eDownload/releases/download/v1.0.0/openNfc-eDownload-1.0.0-fontes.zip).
- [Todas as versões](https://github.com/JannioFSantos/OpenNfc-eDownload/releases).

O executável compilado também está na pasta [`dist/` do repositório](https://github.com/JannioFSantos/OpenNfc-eDownload/tree/main/dist). O manual está em [`MANUAL.md`](MANUAL.md); a arquitetura e o escopo dos testes estão em [`docs/ARQUITETURA.md`](docs/ARQUITETURA.md) e [`docs/VALIDACAO.md`](docs/VALIDACAO.md).

## Executar

Extraia o pacote portátil e abra `openNfc-eDownload.exe`. Mantenha `openNfc-eDownload.exe.config` e `MANUAL.md` na mesma pasta. Requer Windows com .NET Framework 4.8; não requer instalação de banco de dados nem execução como administrador.

1. Selecione SP ou RS e o ambiente.
2. Carregue o A1 PFX/P12 e informe a senha.
3. Confira o CNPJ consultado.
4. Use **Consultar período** ou cole as chaves em **Chaves em lote**.
5. Veja os XMLs, produtos e totais na **Biblioteca e análise**.

| Estado | Certificado e permissão | Forma de coleta |
| --- | --- | --- |
| SP | e-CNPJ do estabelecimento consultado, com CNPJ completo idêntico | Lista por data/hora e download por chave. Janela inicial documentada de 100 dias. |
| RS | A1 e-CPF/e-CNPJ do contabilista com vínculo e autorização eletrônica do cliente no e-CAC | Distribuição por NSU e consulta por chave. Período documentado: mês corrente e dois anteriores; datas filtram a biblioteca local. |

As condições são definidas pelos serviços estaduais. Selecionar datas antigas ou colar uma chave não amplia o período disponibilizado pela SEFAZ. O aplicativo mostra o retorno oficial quando não houver acesso ao XML.

## Recursos

- Chaves em lote copiadas de planilhas; conferência de modelo, CNPJ e dígito verificador.
- Fila persistente, retomada de pendências e consulta separada dos erros.
- Divisão de períodos em SP quando a lista é incompleta.
- Distribuição RS com XML completo solicitado (`indXML=1`, `mod=65`) e cursor confirmado depois da gravação do lote.
- Notas, eventos, produtos, filtro por período, texto/produto, UF, direção e cancelamento.
- Totais de notas sem cancelamento e canceladas separados; exportação CSV e ZIP dos XMLs filtrados.
- Importação de XML e ZIP existentes.
- Consultas sequenciais, espera entre requisições e pausa persistente para consumo indevido.
- Certificado e senha não são incluídos no pacote nem salvos nas preferências.

Leia o [manual de uso](MANUAL.md) e a [documentação técnica](docs/ARQUITETURA.md).

## Compilar e testar

```powershell
.\build.ps1
pwsh -File .\test.ps1 -UiSmoke
.\package.ps1
```

O build usa o compilador C# do .NET Framework do Windows, sem NuGet. Os testes usam documentos e certificado sintéticos, incluindo comunicação HTTPS mTLS em localhost. Os pacotes são montados por uma lista explícita de arquivos do projeto.

## Referências oficiais

- [SP — Serviços de apoio à escrituração da NFC-e](https://portal.fazenda.sp.gov.br/servicos/nfce/Paginas/saenfce.aspx).
- [SP — Nota técnica dos serviços](https://portal.fazenda.sp.gov.br/servicos/nfce/Documents/SAE-NFC-e%20v1.0.0.pdf).
- [RS — Acesso dos contabilistas aos documentos dos clientes](https://atendimento.receita.rs.gov.br/como-os-contabilistas-terao-acesso-as-nf-e-de-seus-clientes).
- [RS — Documentação e schemas do serviço para contabilistas](https://www.sefaz.rs.gov.br/Site/MontaArquivo.aspx?al=l_dwn_doc_ws_contab).
- [RS — Endereços do serviço](https://receita.fazenda.rs.gov.br/conteudo/11797/novos-enderecos-de-web-service-para-download-de-documentos-fiscais-eletronicos-por-contabilistas/termosbusca%3Ddownload).

Licença MIT.
