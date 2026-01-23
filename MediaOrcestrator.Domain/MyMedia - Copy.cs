using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MediaOrcestrator.Domain;

public class MySource
{
    public string Id { get; set; }
    public string TypeId { get; set; }
    public Dictionary<string, string> Settings { get; set; }
}
