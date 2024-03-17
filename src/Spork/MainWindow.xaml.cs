﻿using Spork.ViewModels;
using System;
using System.Windows;

namespace Spork
{
    public partial class MainWindow : Window
    {
        public MainWindow(
            MainWindowViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.WindowLoaded += ViewModel_WindowLoaded;
            viewModel.CloseRequested += ViewModel_CloseRequested;
        }

        public MainWindowViewModel ViewModel
            => (MainWindowViewModel)DataContext;

        private void ViewModel_WindowLoaded(object sender, EventArgs e)
        {
            Width = MinWidth;
            Height = SystemParameters.PrimaryScreenHeight * 0.5;
            Top = 0;
            Left = SystemParameters.PrimaryScreenWidth - Width;
        }

        private void ViewModel_CloseRequested(object sender, EventArgs e)
        {
            Close();
        }
    }
}
