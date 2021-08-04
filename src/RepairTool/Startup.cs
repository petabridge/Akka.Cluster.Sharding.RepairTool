using Microsoft.Extensions.Configuration;

namespace RepairTool
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }
        
        public IConfiguration Configuration { get; }
    }
}