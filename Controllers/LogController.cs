using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
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

        private readonly string _logDir;
        private readonly string _serverName;
        private readonly string[] _children;

        public LogController(ILogger<LogController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _logDir = _configuration["logDir"]?.ToString().TrimEnd(new char[] {'/','\\'});
            _serverName = _configuration["SERVER_NAME"]?.ToString();
            _children = _configuration["children"]?.ToString().Split(",") ?? new string[] {};
        }

        // Gets list of all logs
        [HttpGet]
        public IActionResult Get()
        {
            ApiReturn resp;
            
            try{
                if (string.IsNullOrWhiteSpace(_logDir) || string.IsNullOrWhiteSpace(_serverName)) {

                    string missConfig = string.IsNullOrWhiteSpace(_logDir) ? "log directory" : "server name";
                    resp = CreateResponse(HttpStatusCode.BadRequest, $"Missing configuration setting for {missConfig}");
                    return BadRequest(resp);
                }

                Dictionary<string, IEnumerable<string>> fileLists = new Dictionary<string, IEnumerable<string>>();

                // if this is a parent server then request each child server for list of files
                foreach(string child in _children)
                {
                    if (string.IsNullOrWhiteSpace(child)){
                        continue;
                    }
                    var fileNames = GetServerLogNames(child);
                    fileLists.Add(child, fileNames);
                }
                
                var files = new DirectoryInfo(_logDir).GetFiles()
                .OrderByDescending(_ => _.CreationTimeUtc)
                .Select(_ => _.Name);

                fileLists.Add(_serverName, files);
                
                resp = CreateResponse(HttpStatusCode.OK, "", fileLists);
                
                return Ok(resp);
            } 
            catch (Exception e){
                resp = CreateResponse(HttpStatusCode.InternalServerError, e.Message);
                return StatusCode((int)HttpStatusCode.InternalServerError, resp);
            }
        }

        // Search for logs
        [HttpPost]
        public IActionResult Post(RequestBody body)
        {
            ApiReturn resp;
            
            try {
                if (string.IsNullOrWhiteSpace(_logDir) || string.IsNullOrWhiteSpace(_serverName)) {

                    string missConfig = string.IsNullOrWhiteSpace(_logDir) ? "log directory" : "server name";
                    resp = CreateResponse(HttpStatusCode.BadRequest, $"Missing configuration setting for {missConfig}");
                    return BadRequest(resp);
                }
                else if (string.IsNullOrWhiteSpace(body.FileName)) {
                    resp = CreateResponse(HttpStatusCode.BadRequest, "Requests must contain a file name");
                    return BadRequest(resp);
                }

                int numEvents = body.Filter?.NumEvents ?? 0;

                if (numEvents <= 0){
                    numEvents = 25;
                }
                

                if (System.IO.File.Exists($"{_logDir}\\{body.FileName}")){
                    // return logs in reverse chronological order (assuming logs were appended to file)
                    var lines = System.IO.File.ReadAllLines($"{_logDir}\\{body.FileName}").Reverse();
                    int lineCount = lines.Count();

                    if (!string.IsNullOrEmpty(body.Filter?.Keyword)) {
                        lines = lines.Where(_ => Regex.IsMatch(_, $"{body.Filter.Keyword}"));
                        lineCount = lines.Count();
                    }
                    
                    if (lineCount > numEvents){
                        lines = lines.Take(numEvents);
                        lineCount = lines.Count();
                    }

                    resp = CreateResponse(HttpStatusCode.OK, $"Selected {numEvents} lines, returned {lineCount}", lines);
                    return Ok(resp);
                }
                else{
                    resp = CreateResponse(HttpStatusCode.NotFound, $"No file with the name {body.FileName} exists in the target directory");
                    return NotFound(resp);
                }
            }
            catch (Exception e) {
                resp = CreateResponse(HttpStatusCode.InternalServerError, e.Message);
                return StatusCode((int)HttpStatusCode.InternalServerError, resp);
            }
        }

        private IEnumerable<string> GetServerLogNames(string child){
            IEnumerable<string> logFileNames = new string[] {};
            
            // make get request to child servers

            return logFileNames;
        }

        private ApiReturn CreateResponse(HttpStatusCode statusCode, string message, object data = null) {
            return new ApiReturn(){
                ServerName = _serverName,
                StatusCode = statusCode,
                Message = message,
                Data = data
            };
        }
    }
}
