using System;

namespace Log_Collection
{
    public class ApiReturn
    {
        // Http response status code
        public int StatusCode { get; set; }

        // User readable status code, error messagees, etc
        public string Message { get; set; }

        // object for any data returned from response
        public object Data { get; set; }
    }

    //public class
    /*
        {
            log: string,
            server: string
        }
    */ 
}
