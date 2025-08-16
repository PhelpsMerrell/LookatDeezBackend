using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LookatDeezBackend.Data.Models.Requests
{
    public class AddItemRequest
    {
        public string Title { get; set; }
        public string Url { get; set; }
    }
}
