﻿using System;

namespace Spork.ViewModels
{
    [Serializable]
    public class OpenWebSiteItemViewModel : InstallItemViewModel
    {
        private string _targetUrl;

        public string TargetUrl
        {
            get => _targetUrl;
            set => SetProperty(ref _targetUrl, value);
        }
    }
}
