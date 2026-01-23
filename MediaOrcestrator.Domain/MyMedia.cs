using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MediaOrcestrator.Domain;

public class MyMedia
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }

    public List<MyMediaLinkToSource> Sources { get; set; }
}
