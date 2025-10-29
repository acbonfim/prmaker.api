namespace Cliqx.BuildingBlocks.GlobalModels
{
    public class Retorno
    {
        public string Status { get; set; }
        public bool Finished { get; set; }
        public bool Repeat { get; set; }
        public string Mensagem { get; set; }
        public string NovaMensagemUsuario { get; set; }
        public string NovaImagem { get; set; }
        public string AgentId { get; set; }
        public int GoTo { get; set; }
        public Dictionary<string, string> CustomVariables { get; set; }
        public MensagemInterativa MensagemInterativa { get; set; }

        public static Retorno RetornoOk()
        {
            return new Retorno { Status = "Ok" };
        }

        public void SetStatusRetorno(StatusRetornoEnum retorno)
        {
            NovaMensagemUsuario = retorno.ToString().ToLower();
        }
        

    }
}