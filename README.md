# Log_Collection
A customer has asked you for a way to provide on-demand monitoring of various unix-based servers without having to log into each individual machine and opening up the log files found in /var/log.

# Endpoints

- GET \log returns a list of all files in the child and parent servers

- POST \log returns log entries following specific criteria

```
Post Body:
{
    "FileName": "test.log",
    "Filter": {
        "Keyword": "1",
        "NumEvents": 10,
        "ServerNames": ["NoServer"]
    }
}
```
- Filename = string name of file to search
- Keyword = string of word to search for
- NumEvents = int number of events to return (defaults to 25)
- ServerNames = string array of child servers to search (defaults to all)

# Running

- `dotnet run` and make REST requests to http://localhost:5000 or https://localhost:5001
- `docker-compose up` to test parent-child server requests.  
    - http://localhost:5000 is the parent
    - http://localhost:5001 and http://localhost:5002 are children

# Testing

- import postman library in `Log Collection.postman_collection.json` or run curl requests from the terminal