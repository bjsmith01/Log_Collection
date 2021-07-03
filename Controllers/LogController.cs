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
            _logDir = _configuration["LOGDIR"]?.ToString().TrimEnd(new char[] {'/','\\'});
            _serverName = _configuration["SERVER_NAME"]?.ToString();
            _children = _configuration["CHILDREN"]?.ToString().Split(",") ?? new string[] {};
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
                    fileLists.Add(fileNames.Item1, fileNames.Item2);
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
                Dictionary<string, IEnumerable<string>> lines = new Dictionary<string, IEnumerable<string>>();
                int lineCount;
                int numEvents = body.Filter?.NumEvents ?? 0;

                if (numEvents <= 0){
                    numEvents = 25;
                }
                // only make requests to other servers if the entered name isn't the current server
                // this requires that the domain contains the server name, which is fine for a proof of concept, but not prod
                // ideally some kind of dictionary would be build containing {serverName : url}
                if ( (body.Filter?.ServerNames?.Any(_ => !_.Contains(_serverName)) & _children.Any()) ?? false) {
                    foreach(string serverName in body.Filter.ServerNames.Where(_ => !_.Contains(_serverName))){
                        if (_children.Any(_ => serverName.Contains(_))) {
                            //Console.WriteLine(serverName);
                            var logs = await GetServerLogs(serverName, body);
                            lines.Add(logs.Item1, logs.Item2);
                        }
                    }
                }
                else 
                {
                    // 404 if request is missing info
                    // with more time this would be refactored to throw custom errors that when caught create a response
                    if (string.IsNullOrWhiteSpace(_logDir) || string.IsNullOrWhiteSpace(_serverName)) {
                        string missConfig = string.IsNullOrWhiteSpace(_logDir) ? "log directory" : "server name";
                        resp = CreateResponse(HttpStatusCode.BadRequest, $"Missing configuration setting for {missConfig}");
                        return BadRequest(resp);
                    }
                    else if (string.IsNullOrWhiteSpace(body.FileName)) {
                        resp = CreateResponse(HttpStatusCode.BadRequest, "Requests must contain a file name");
                        return BadRequest(resp);
                    }
                    
                    if (System.IO.File.Exists($"{_logDir}/{body.FileName}")){
                        // return logs in reverse chronological order (assuming logs were appended to file)
                        var tempLines = System.IO.File.ReadAllLines($"{_logDir}/{body.FileName}").Reverse();
                        lineCount = tempLines.Count();

                        if (!string.IsNullOrEmpty(body.Filter?.Keyword)) {
                            tempLines = tempLines.Where(_ => Regex.IsMatch(_, $"{body.Filter.Keyword}"));
                            lineCount = tempLines.Count();
                        }
                        
                        // only take the entered number of events if there are that many lines in the file
                        if (lineCount > numEvents){
                            tempLines = tempLines.Take(numEvents);
                        }

                        lines.Add(_serverName, tempLines);
                    }
                    else{
                        resp = CreateResponse(HttpStatusCode.NotFound, $"No file with the name {body.FileName} exists in the target directory");
                        return NotFound(resp);
                    }
                }

                lineCount = lines.Sum(_ => _.Value.Count());
                resp = CreateResponse(HttpStatusCode.OK, $"Selected {numEvents} lines, returned {lineCount}", lines);
                return Ok(resp);
            }
            catch (UriFormatException e){
                resp = CreateResponse(HttpStatusCode.InternalServerError, $"Invalid configuration for child server: {e.Message}");
                return StatusCode((int)HttpStatusCode.InternalServerError, resp);
            }
            catch (FileNotFoundException f) {
                resp = CreateResponse(HttpStatusCode.NotFound, f.Message);
                return BadRequest(resp);
            }
            catch (Exception e) {
                resp = CreateResponse(HttpStatusCode.InternalServerError, e.Message);
                return StatusCode((int)HttpStatusCode.InternalServerError, resp);
            }
        }

        // make web request to child server to get log file names
        private async Task<(string, IEnumerable<string>)> GetServerLogNames(string child){
            IEnumerable<string> logFileNames = new string[] {};
            string childName = string.Empty;
            
            using (var client = new HttpClient()) {
                client.BaseAddress = new Uri(child);                

                var response = await client.GetAsync("log");
                if (response != null) {
                    var resp = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
                    childName = resp.GetProperty("ServerName").GetString();
                    var logDict = resp.GetProperty("Data").GetProperty(childName);
                    logFileNames = logDict.EnumerateArray().Select(_ => _.ToString());
                }
            }

            return (childName, logFileNames);
        }

        // Make web request to child server to get logs. 
        private async Task<(string, IEnumerable<string>)> GetServerLogs(string child, RequestBody body){
            IEnumerable<string> logs = new string[] {};
            string childName = string.Empty;
            string message = string.Empty;
            
            try {
                // make get request to child servers
                using (var client = new HttpClient())
                {
                    client.BaseAddress = new Uri(child);
                    var content = new StringContent(
                        JsonSerializer.Serialize(body),
                        System.Text.Encoding.UTF8,
                        "application/json"
                    );

                    var response = await client.PostAsync("log", content);
                    if (response != null) {
                        var resp = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
                        //Console.WriteLine(resp);
                        childName = resp.GetProperty("ServerName").GetString();
                        message = resp.GetProperty("Message").GetString();
                        var logDict = resp.GetProperty("Data").GetProperty(childName);
                        logs = logDict.EnumerateArray().Select(_ => _.ToString());
                    }
                }

                return (childName, logs);
            }
            catch (UriFormatException e) 
            {               
                /* 
                    super generic error catching.  would replace with custom error type with more time (ie BadRequestException)
                    get property data will fail if the file does not exist on the target server
                    throw a different error to be caught by parent function
                */
                throw e;
            }
            catch (Exception e) 
            {
                //Console.WriteLine(e.GetType());
                /* 
                    super generic error catching.  would replace with custom error type with more time (ie BadRequestException)
                    get property data will fail if the file does not exist on the target server
                    throw a different error to be caught by parent function
                */
                throw new FileNotFoundException(message);
            }
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
