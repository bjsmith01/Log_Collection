using System;
using System.Net;

namespace Log_Collection
{
    public class ApiReturn
    {
        private int _statusCode;
        private string _statusCodeText;

        // Http response status code
        public HttpStatusCode StatusCode { 
            get { return (HttpStatusCode)_statusCode; }
            set 
            { 
                _statusCode = (int)value;
                _statusCodeText = Enum.GetName(typeof(HttpStatusCode), value);
            }
        }

        public string StatusCodeText { get {return _statusCodeText; } }

        public string ServerName { get; set; }

        // User readable status code, error messagees, etc
        public string Message { get; set; }

        // object for any data returned from response
        public object Data { get; set; }
    }

    public class RequestBody
    {
        public Filter Filter { get; set; }
        public string FileName { get; set; }
    }

    public class Filter 
    {
        public string Keyword { get; set; }
        public int NumEvents { get; set; }
    }
     
}
