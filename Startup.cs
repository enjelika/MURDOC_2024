using MURDOC_2024.Model.MICA.Services;
using MURDOC_2024.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MURDOC_2024
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use it to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IIAIDecisionService>(serviceProvider =>
                new IAIDecisionService(
                    Configuration["PythonPath"],
                    Configuration["IAIScriptPath"]
                ));

            // Only register the necessary services
            services.AddScoped<IMICAService, MICAService>();
            services.AddScoped<DetectionPipeline>();
        }

        // This method gets called by the runtime. Use it to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            // Configure your application's request pipeline here
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            // Add other middleware configuration as needed
        }
    }
}
