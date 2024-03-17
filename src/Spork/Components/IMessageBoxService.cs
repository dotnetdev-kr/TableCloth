﻿using System.Windows;

namespace Spork.Components
{
    public interface IMessageBoxService
    {
        MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult, MessageBoxOptions options = MessageBoxOptions.None);
    }
}