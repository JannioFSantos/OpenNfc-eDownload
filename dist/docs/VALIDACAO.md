# Validação da versão 1.0.0

Resultado: **72 verificações offline aprovadas**, com `pwsh -File .\test.ps1 -UiSmoke`.

## Escopo verificado

- Validação de CNPJ, chave NFC-e SP/RS, modelo 65 e dígito verificador.
- Colagem de planilhas, prefixo NFe, remoção de duplicatas e rejeição de notação científica.
- XML sem DTD/entidades externas, GZip e gravação atômica.
- Payloads SP de listagem/download e RS de distribuição/consulta por chave validados contra XSDs oficiais obtidos separadamente para o desenvolvimento.
- Envelopes document/literal diretos e envelopados, SOAP 1.1/1.2, action extraída do WSDL, Fault e XML escapado em Result.
- SP: divisão de intervalos incompletos, deduplicação, preservação de retornos, XML com produtos, eventos e aviso de minuto incompleto.
- Fila persistente, erro específico por chave, sucesso sem XML mantido pendente, rejeição de ambiente divergente e pausa 656 persistida.
- RS: lotes 50+1, continuidade por NSU, pausa de 15 minutos compartilhada entre filtros, consulta por chave sem consumir cursor, regressão de NSU e falha parcial sem avançar cursor.
- Biblioteca, cancelamentos registrados, filtros, produtos, exportação CSV/ZIP e importação.
- A1 sintético com SAN ICP-Brasil, CNPJ exato em SP e CNPJ de cliente diferente em RS.
- Comunicação HTTPS real em localhost com certificado sintético: autenticação mTLS no WSDL e SOAP, action e corpo transmitidos conforme contrato sintético.
- Bloqueio de envio do A1 para host arbitrário ou endereço sem HTTPS.
- Interface com cinco abas e três tabelas, troca SP/RS e tamanho mínimo.

As telas de conexão e biblioteca também foram conferidas visualmente.

## Limites

Não foram efetuadas consultas autenticadas aos serviços fiscais reais. Os testes utilizam exclusivamente XMLs e A1 sintéticos. O resultado valida os fluxos locais e o transporte testado, sem afirmar autorização ou disponibilidade de documentos em produção. O primeiro acesso real depende do A1 correto em SP e do vínculo/autorização do contabilista no RS.

## Reproduzir

```powershell
pwsh -File .\test.ps1 -UiSmoke
```

O script gera o A1 sintético numa pasta temporária, executa a suíte e remove o PFX no bloco `finally`. O relatório e os XMLs sintéticos ficam apenas na pasta temporária indicada no resultado.

Se houver uma pasta de pesquisa `reference/` com os XSDs oficiais SP/RS, o script também executa a validação dos pedidos pelos schemas. Essa pasta não é distribuída nos ZIPs. Sem ela, a suíte continua cobrindo os contratos por fixtures, mas informa um número menor de verificações. Consulte os links oficiais do README para obter os schemas.
