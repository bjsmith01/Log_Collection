using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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

        // Gets list of all logs
        [HttpGet]
        public IActionResult Get()
        {
            string logDir = _configuration["logDir"].ToString();
            string serverName = _configuration["SERVER_NAME"].ToString();
            var resp = new ApiReturn(){
                ServerName = serverName
            };
            try{
                var files = new DirectoryInfo(logDir).GetFiles().Select(f => f.Name);

                resp.StatusCode = (int)HttpStatusCode.OK;
                resp.StatusCodeText = Enum.GetName(typeof(HttpStatusCode), HttpStatusCode.OK);
                resp.Data = files;

                return Ok(resp);
            } 
            catch (Exception e){
                resp.StatusCode = (int)HttpStatusCode.InternalServerError;
                resp.StatusCodeText = Enum.GetName(typeof(HttpStatusCode), HttpStatusCode.InternalServerError);                
                resp.Message = e.Message;

                return StatusCode((int)HttpStatusCode.InternalServerError, resp);
            }
        }
    }
}
