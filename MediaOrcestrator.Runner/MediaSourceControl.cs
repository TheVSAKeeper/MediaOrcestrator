using MediaOrcestrator.Core.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MediaOrcestrator.Runner
{
    public partial class MediaSourceControl : UserControl
    {
        public MediaSourceControl()
        {
            InitializeComponent();
        }

        public void SetMediaSource(IMediaSource source)
        {
            label1.Text = source.Name;
        }
    }
}
