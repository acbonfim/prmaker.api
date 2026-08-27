 using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;
using Cime.BuildingBlocks.GlobalModels;

namespace Cime.BuildingBlocks.Swagger
{
    public class UtilsSwaggerHTML
    {
        public string Linha(int vezes = 1)
        {
            var t = "";
            for (int i = 0; i < vezes; i++)
            {
                t += "<br>";
            }
            return t;
        }

        public string Espaco(int vezes = 1)
        {
            var t = "";
            for (int i = 0; i < vezes; i++)
            {
                t += "&nbsp;";
            }
            return t;
        }

        public string Negrito(string t)
        {
            return $"**{t}**";
        }

        public string Link(string sessionId, string text)
        {
            return $"<a href='#{sessionId}'><strong>{text}</strong></a>";
        }

        public string Paragrafo(string t)
        {
            return $"<p>{t}</p>";
        }

        public string Titulo(string t, [Range(1, 5)] int tam)
        {
            return $"<h{tam}>{t}</h{tam}>";
        }

        public string Div(string t)
        {
            return $"<div><font size='2'>{t}</font></div>";
        }

        public string MontaDescricaoAPI()
        {
            var t = new StringBuilder();


            t.Append($"{Linha(1)}");

            t.Append($"{Titulo("Boilerplate para criação de microsserviços", 1)}");

            t.Append($"{Linha(1)}");

            /// INTRODUÇÃO

            t.Append($"{Titulo("Introdução", 2)}");

            t.Append($"{Div(@"Aqui ficará a introdução sobre o que é o serviço.")}");

            t.Append($"{Linha(1)}");

            /// AUTENTICAÇÃO

            t.Append($"{Titulo("Autenticação", 2)}");

            t.Append($"{Div(@"Todas as chamadas de API exigem uma chave de acesso de API (ApiKey). 
            Essa chave deve ser colocada no cabeçalho de todas requisições:")}");

            t.Append($"{Div(@"x-api-key: your-api-key")}");

            t.Append($"{Div(@"Para ter acesso a chave, você precisa ter um usuário e senha que é fornecido pela SOLVACE.
            Essa chave é gerada dentro do SOLVACE, na sessão de Integrações/Api Key")}");

            t.Append($"{Div(@"Entre em contato com o administrador para obter o seu acesso.")}");

            /// INSTRUÇÕES

            t.Append($"{Linha(1)}");

            t.Append($"{Titulo("Instruções", 2)}");

            t.Append($"{Paragrafo(@"Aqui ficará as instruções para uso e consumo do serviço.")}");


            t.Append($"{Div($@"1 - Use o nome do elemento para criar um link como este {Link("operations-Default-get_api_v1_Default_Get", "Default Get")} para levar ao endpoint definido")}");
            t.Append($"{Div($@"2 - Também pode utilizar link para os models ({Link("model-BaseEntity", "BaseEntity")})")}");

            /// ERROS

            t.Append($"{Linha(1)}");

            t.Append($"{Titulo("Códigos de status HTTP", 2)}");

            t.Append($"{Paragrafo("As APIs poderão retornar os seguintes códigos de status HTTP:")}");

            t.Append($@"
                <table>
                <thead>
                    <tr>
                    <th>Código</th>
                    <th>Descrição</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                    <td>200 (OK)</td>
                    <td>Requisição processada com sucesso. A resposta é diferente para cada API, verifique a documentação específica de cada operação</td>
                    </tr>
                    <tr>
                    <td>400 (Bad Request)</td>
                    <td>Erro de sintaxe. Por exemplo, quando um campo obrigatório não for preenchido. O corpo da resposta indicará qual parâmetro está inválido ou não foi preenchido.</td>
                    </tr>
                    <tr>
                    <td>401 (Unauthorized)</td>
                    <td>A chave de API não foi fornecida ou é inválida</td>
                    </tr>
                    <tr>
                    <td>403 (Forbidden)	</td>
                    <td>A chave de API é válida, mas tem privilégios insuficientes para completar a operação solicitada</td>
                    </tr>
                    <tr>
                    <td>422 (Unprocessable Entity)</td>
                    <td>Erro de lógica de négocio. A resposta conterá uma mensagem e um código de erro conforme defindo em {Link("model-error", "Error")}</td>
                    </tr>
                    <tr>
                    <td>500 (Internal Server Erro)</td>
                    <td>Erro interno no servidor. Instabilidade técnica do lado do servidor</td>
                    </tr>
                    <tr>
                    <td>501 (Not Implemented)</td>
                    <td>A função solicitada não foi implementada</td>
                    </tr>
                </tbody>
                </table>
            ");

            t.Append($"{Linha(1)}");

            t.Append($"{Titulo("Códigos de erro", 2)}");

            t.Append($"{Paragrafo("Alguns dos códigos de erro retornados em uma resposta com status 422 podem ser encontrados abaixo:")}");

            t.Append($"<ul>");

            foreach (ErrorCodeEnum item in Enum.GetValues(typeof(ErrorCodeEnum)))
            {
                string description = GetEnumDescription(item);

                t.Append($"<li>{$"{Negrito(item.ToString())}: {(description)}"}</li>");
            }

            t.Append($"</ul>");


            

            return t.ToString();
        }

        private static string GetEnumDescription(Enum value)
        {
            FieldInfo fieldInfo = value.GetType().GetField(value.ToString());

            if (fieldInfo.GetCustomAttribute(typeof(DescriptionAttribute)) is DescriptionAttribute attribute)
            {
                return attribute.Description;
            }

            return value.ToString();
        }
    }
}