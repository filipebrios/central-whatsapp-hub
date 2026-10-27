MODUX 1.9.6 beta • Windows x64 • ARANY

Correção da edição de serviços que atravessam a meia-noite.

- Eventos com horário definido e duração de 15 minutos a 24 horas podem ser editados mesmo terminando no dia seguinte, como 23:30–00:30. O editor preserva a data inicial, o horário e a duração ao alterar os dados.
- O término aparece com data e hora, acompanhado de dia seguinte quando necessário. Mudar data, horário ou duração atualiza a indicação antes de salvar.
- Cadastro/edição de WhatsApp, vínculo/reconciliação do atendimento e preparação da confirmação também aceitam esse intervalo. Mantidos IDs, revisão e ETag; sem criar outro evento ou alterar a série inteira.
- Eventos de dia inteiro com várias datas e durações fora do limite continuam sendo editados no Google Agenda. Controles de conflito e confirmação explícita permanecem.

Instale sobre a versão atual. Mantém endereço na mensagem, identidade São Bento no link, campo de WhatsApp, fila e demais melhorias da 1.9.5. Envio de WhatsApp continua manual; testes usam eventos fictícios e não alteram compromissos reais. Beta para validação no Windows.
