# 0072 — Pergunte à Base Solvace com anexos (imagem, arquivo, voz)

## Pedido
No campo de busca do Pergunte: arrastar e soltar, colar da área de transferência (print), anexar arquivos e falar; a IA
interpreta tudo com o modelo configurado (hoje Haiku) gastando pouco.

## O que o modelo aceita (decisão)
- **Imagem** (PNG/JPEG/WebP/GIF): sim — o Claude lê imagem. Redimensionada no navegador (lado maior ≤ 1.500 px, JPEG).
- **Texto/log/CSV/JSON/MD e PDF**: sim (texto vai no prompt; PDF como documento).
- **Áudio**: o Claude **não** recebe áudio. Etapa 1: **ditado no navegador** (Web Speech API, pt-BR) preenche o campo — sem
  upload de áudio, sem custo. Arquivo de áudio com transcrição no servidor fica **fora de escopo** (outro provedor/chave).
- Haiku **não aceita `effort`** (aprendido na 0071) — nenhum ajuste de esforço por chamada.

## Custo (sem chamada extra)
Os anexos vão na chamada de **plano** que o Pergunte já faz; ela devolve, além dos termos, uma **descrição do que viu**
(texto de erro, tela, módulo). A busca usa a descrição e a chamada da **resposta** continua só de texto — a imagem não é
reenviada. ~1–1,6 mil tokens de entrada por imagem (uma pergunta já usa ~20 mil).

## Limites e privacidade
- Até 4 anexos; imagem ≤ 5 MB depois de reduzida; texto ≤ 100 KB (cortado, com aviso); PDF ≤ 5 MB e ≤ 10 páginas. Sem vídeo.
- Anexo **nunca é gravado** (nem em `ArchitectureQuestion`, nem em log): só o texto da pergunta é registrado.
- Pergunta com anexo **não usa** o cache de 15 min da 0071.
- Sai pela chave de API do usuário (plugin pessoal), como o resto do Pergunte.
- Provedor sem suporte (Gemini/OpenAI hoje, ou modelo sem visão): `ask/status` devolve `supportsAttachments=false` e a tela
  esconde o recurso; o endpoint recusa anexos com 409 e mensagem clara.

## Aceite
1. Colar um print de erro no campo, perguntar "o que é isso?" e receber resposta que cita o erro lido e o módulo.
2. Arrastar um `.log`/`.txt` e um PDF; a resposta considera o conteúdo.
3. Ditar a pergunta pelo microfone (pt-BR); o texto aparece no campo para conferir antes de enviar.
4. Sem anexo, o Pergunte se comporta como antes (mesmo prompt, mesmo cache).
5. Anexo recusado (tipo/tamanho/quantidade) mostra o motivo na tela, sem chamar a IA.
