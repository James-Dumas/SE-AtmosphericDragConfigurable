using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Sandbox.ModAPI;

namespace dev.jamac.AtmosphericDragConfigurable
{
    public class ChatCommandHandler
    {
        private static readonly Regex commandRegex = new Regex(@"\/[a-zA-Z][a-zA-Z0-9_-]*(?:\s+.*)?");
        private static readonly Regex splitRegex = new Regex(@"\s+");

        public Dictionary<string, Action<string[]>> Commands { get; private set; }

        public ChatCommandHandler()
        {
            Commands = new Dictionary<string, Action<string[]>>();
            MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
        }

        private void OnMessageEntered(string messageText, ref bool sendToOthers)
        {
            if (!commandRegex.IsMatch(messageText)) { return; }
            string[] args = splitRegex.Split(messageText.Substring(1).Trim());
            if(Commands.ContainsKey(args[0]))
            {
                sendToOthers = false;
                Commands[args[0]](args);
            }
        }

        public void Stop()
        {
            MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
        }
    }
}
