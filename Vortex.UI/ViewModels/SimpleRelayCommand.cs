using System;
using System.Windows.Input;

// ViewModel layer for UI binding
namespace Vortex.UI.ViewModels
{
    // Simple command implementation for action execution
    public class SimpleRelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        public SimpleRelayCommand(Action<object> execute)
        {
            _execute = execute;
        }
        public bool CanExecute(object parameter) => true;
        public event EventHandler CanExecuteChanged { add { } remove { } }
        public void Execute(object parameter) => _execute(parameter);
    }
}