using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace JabbR.Client.Models
{
    public class Message
    {
        public string Id { get; set; }
        public string Content { get; set; }
        public DateTimeOffset When { get; set; }
        public User User { get; set; }
    }
}
