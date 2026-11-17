namespace solvace.azure.domain.Requests;

/// <summary>
/// Card 78525: grava a especificação da User Story nos campos do card. <see cref="Document"/> = o texto inteiro, com uma
/// linha de título por seção ("1. High Level Business Specification (PM)", "## Technical Specification"...) — o PRMake
/// separa pelas seções configuradas. <see cref="Fields"/> = seção (ou nome de referência do campo) → texto. Markdown vira
/// HTML nos campos das seções; outro campo (ex.: picklist) vai como veio. Seção vazia não apaga o campo.
/// </summary>
public class WriteSpecRequest
{
    public string? Document { get; set; }
    public Dictionary<string, string>? Fields { get; set; }

    /// <summary>
    /// Cards onde estão os arquivos citados como <c>anexo:nome</c> (padrão: o próprio card) — ex.: a análise feita no
    /// 78505 e gravada na US 78525. Cada imagem sobe como anexo do DevOps e o texto passa a apontar para ele.
    /// </summary>
    public List<string>? FilesFrom { get; set; }
}
