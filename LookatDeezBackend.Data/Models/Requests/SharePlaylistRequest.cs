using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LookatDeezBackend.Data.Models.Requests
{
    public class SharePlaylistRequest
    {
        public string UserId { get; set; }
        public string Permission { get; set; } = "view";
    }

}
