using System.ComponentModel;
using System.Windows.Controls;

using TombEditor.ViewModels;

namespace TombEditor.Views
{
    public partial class ObjectBrushToolboxView : UserControl
    {
        public ObjectBrushToolboxView()
        {
            InitializeComponent();

            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                var viewModel = new ObjectBrushToolboxViewModel();
                DataContext = viewModel;
                Unloaded += (_, _) => viewModel.Cleanup();
            }
        }
    }
}
