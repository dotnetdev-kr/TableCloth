﻿using System;
using TableCloth.Models;

namespace Spork.Components.Implementations
{
    public sealed class CommandLineArguments : ICommandLineArguments
    {
        private readonly Lazy<CommandLineArgumentModel> _argvModelFactory
            = new Lazy<CommandLineArgumentModel>(() => CommandLineArgumentModel.ParseFromArgv());

        public CommandLineArgumentModel Current
            => _argvModelFactory.Value;
    }
}
