namespace solvace.knowledge.domain.Entities;

/// <summary>
/// O Guia de um projeto (0038): seções em linguagem simples para QA, gestores e suporte (público <c>human</c>). Ficam
/// só na tela, na busca e no "Pergunte" — o espelho das skills leva apenas as seções técnicas.
/// </summary>
public static class ArchitectureGuideTemplate
{
    public record Item(string Key, string Title, int Order, string Purpose);

    public static readonly IReadOnlyList<Item> Sections =
    [
        new("guia-o-que-e", "O que é e para que serve", 510,
            "o que o sistema faz para o negócio, quem usa (perfis), as principais telas/funcionalidades e um exemplo do dia a dia da fábrica"),
        new("guia-como-funciona", "Como funciona, passo a passo", 520,
            "os fluxos principais do ponto de vista do usuário, os estados/status e o que muda em cada um, o que acontece automaticamente"),
        new("guia-regras", "Regras de negócio", 530,
            "as regras em linguagem simples (quem pode fazer o quê, prazos, aprovações, validações), citando os artigos do Knowledge Center como ART-n"),
        new("guia-conexoes", "Com quem conversa", 540,
            "com quais outros sistemas ele se comunica e como: o que dispara cada comunicação, quem fica escutando, se é na hora ou em segundo plano (fila/evento/Lambda/rotina agendada), e-mails/notificações e o que acontece se falhar — explique cada termo técnico numa frase"),
        new("guia-como-testar", "Como testar", 550,
            "cenários para QA (caminho feliz e casos de borda), onde conferir o resultado, pré-requisitos e dados de teste, cuidados"),
        new("guia-perguntas", "Perguntas frequentes", 560,
            "dúvidas comuns de suporte/QA com respostas curtas (só se houver material)")
    ];

    /// <summary>Só no projeto <c>ecossistema</c>.</summary>
    public static readonly Item Glossary = new("guia-glossario", "Glossário", 570,
        "termos técnicos que aparecem na base explicados para quem não é da área (fila, evento, Lambda, API, Cognito, SSO, revamp, legado...)");

    public static IReadOnlyList<Item> For(string projectKey) =>
        projectKey == "ecossistema" ? [.. Sections, Glossary] : Sections;

    public static Item? Find(string key) => Sections.FirstOrDefault(s => s.Key == key) ?? (Glossary.Key == key ? Glossary : null);

    /// <summary>Regras de escrita do Guia (prompt da IA e template da skill).</summary>
    public const string WritingRules = """
        Escreva para QA, gestores e suporte — pessoas que não programam:
        - português do Brasil, frases curtas, voz ativa, listas e passo a passo; exemplos do dia a dia da fábrica;
        - sem nomes de tabelas, classes, namespaces, endpoints ou caminhos de arquivo (cite a tela/funcionalidade pelo nome que o usuário vê);
        - termo técnico inevitável (fila, evento, Lambda, API, Cognito, SSO) → explique numa frase na primeira vez;
        - diagrama só se ajudar: ```mermaid flowchart LR``` pequeno, rótulos em português;
        - regras de negócio citam o artigo do Knowledge Center como ART-n;
        - não invente: o que a documentação não diz vira "a confirmar"; nunca credenciais, senhas ou dados de cliente.
        """;
}
