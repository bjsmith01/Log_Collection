using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
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
        public async Task<IActionResult> Get()
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
                    var fileNames = await GetServerLogNames(child);
                    fileLists.Add(child, fileNames);
                }
                
                var files = new DirectoryInfo(_logDir).GetFiles()
                .OrderByDescending(_ => _.CreationTimeUtc)
                .Select(_ => _.Name);

                fileLists.Add(_serverName, files);
                
                resp = CreateResponse(HttpStatusCode.OK, "", fileLists);
                
                return Ok(resp);
            }
            catch (UriFormatException e){
                resp = CreateResponse(HttpStatusCode.InternalServerError, $"Invalid configuration for child server: {e.Message}");
                return StatusCode((int)HttpStatusCode.InternalServerError, resp);
            }
            catch (Exception e){
                resp = CreateResponse(HttpStatusCode.InternalServerError, e.Message);
                return StatusCode((int)HttpStatusCode.InternalServerError, resp);
            }
        }

        // Search for logs
        [HttpPost]
        public async Task<IActionResult> Post(RequestBody body)
        {
            ApiReturn resp;
            
            try {
                IEnumerable<string> lines;
                int lineCount;
                int numEvents = body.Filter?.NumEvents ?? 0;

                if (numEvents <= 0){
                    numEvents = 25;
                }
                // only make requests to other servers if the entered name isn't the current server
                if (!string.IsNullOrWhiteSpace(body.Filter?.ServerName) && _serverName != body.Filter.ServerName)
                {
                    lines = await GetServerLogs(body.Filter.ServerName, body);
                }
                else 
                {
                    // 404 if request is missing info
                    if (string.IsNullOrWhiteSpace(_logDir) || string.IsNullOrWhiteSpace(_serverName)) {
                        string missConfig = string.IsNullOrWhiteSpace(_logDir) ? "log directory" : "server name";
                        resp = CreateResponse(HttpStatusCode.BadRequest, $"Missing configuration setting for {missConfig}");
                        return BadRequest(resp);
                    }
                    else if (string.IsNullOrWhiteSpace(body.FileName)) {
                        resp = CreateResponse(HttpStatusCode.BadRequest, "Requests must contain a file name");
                        return BadRequest(resp);
                    }
                    
                    if (System.IO.File.Exists($"{_logDir}\\{body.FileName}")){
                        // return logs in reverse chronological order (assuming logs were appended to file)
                        lines = System.IO.File.ReadAllLines($"{_logDir}\\{body.FileName}").Reverse();
                        lineCount = lines.Count();

                        if (!string.IsNullOrEmpty(body.Filter?.Keyword)) {
                            lines = lines.Where(_ => Regex.IsMatch(_, $"{body.Filter.Keyword}"));
                            lineCount = lines.Count();
                        }
                        
                        // only take the entered number of events if there are that many lines in the file
                        if (lineCount > numEvents){
                            lines = lines.Take(numEvents);
                        }
                    }
                    else{
                        resp = CreateResponse(HttpStatusCode.NotFound, $"No file with the name {body.FileName} exists in the target directory");
                        return NotFound(resp);
                    }
                }

                lineCount = lines.Count();
                resp = CreateResponse(HttpStatusCode.OK, $"Selected {numEvents} lines, returned {lineCount}", lines);
                return Ok(resp);
            }
            catch (Exception e) {
                resp = CreateResponse(HttpStatusCode.InternalServerError, e.Message);
                return StatusCode((int)HttpStatusCode.InternalServerError, resp);
            }
        }

        // make web request to child server to get log file names
        private async Task<IEnumerable<string>> GetServerLogNames(string child){
            IEnumerable<string> logFileNames = new string[] {};
            
            using (var client = new HttpClient()) {
                client.BaseAddress = new Uri(child);                

                var response = await client.GetAsync("logs");
                if (response != null) {
                    var resp = JsonSerializer.Deserialize<ApiReturn>(await response.Content.ReadAsStringAsync());
                    var logDict = (Dictionary<string,IEnumerable<string>>) resp.Data;
                    logFileNames = logDict[child];
                }
            }

            return logFileNames;
        }

        // Make web request to child server to get logs. 
        private async Task<IEnumerable<string>> GetServerLogs(string child, RequestBody body){
            IEnumerable<string> logs = new string[] {};
            
            // make get request to child servers
            using (var client = new HttpClient()){
                client.BaseAddress = new Uri(child);
                var content = new StringContent(
                    JsonSerializer.Serialize(body),
                    System.Text.Encoding.UTF8,
                    "application/json"
                );

                var response = await client.PostAsync("logs", content);
                if (response != null) {
                    var resp = JsonSerializer.Deserialize<ApiReturn>(await response.Content.ReadAsStringAsync());
                    logs = (IEnumerable<string>)resp.Data;
                }
            }

            return logs;
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
