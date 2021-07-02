using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Log_Collection.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class LogController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<LogController> _logger;

        public LogController(ILogger<LogController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        [HttpGet]
        public ApiReturn Get()
        {
            string test = _configuration["logDir"].ToString();

            var resp = new ApiReturn(){
                StatusCode = 200,
                Message = "OK",
                Data = test
            };
            return resp;
        }
    }
}
