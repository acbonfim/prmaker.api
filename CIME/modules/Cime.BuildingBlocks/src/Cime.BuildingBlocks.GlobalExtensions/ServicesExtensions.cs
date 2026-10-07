using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Cime.BuildingBlocks.GlobalExtensions
{
    public static class ServicesExtensions
    {
        public static IServiceCollection AddGlobalServices(this IServiceCollection services)
        {

            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            services.AddControllers().AddJsonOptions(x =>
            {
                x.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
                // 0070: texto grande escrito em segmentos (sem alugar 6× o tamanho do documento)
                x.JsonSerializerOptions.Converters.Add(new SegmentedStringConverter());
            });

            services.AddMemoryCache();
            services.AddResponseCaching();
            services.AddHttpClient();




            return services;
        }
    }
}