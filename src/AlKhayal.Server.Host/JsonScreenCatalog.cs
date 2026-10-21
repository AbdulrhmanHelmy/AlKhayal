using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AlKhayal.Application.Users;
using Newtonsoft.Json.Linq;

namespace AlKhayal.Server.Host
{
    public sealed class JsonScreenCatalog : IScreenCatalog
    {
        private readonly IReadOnlyList<ScreenDefinition> _screens;
        public JsonScreenCatalog(string frontendPath)
        {
            var path = Path.Combine(frontendPath ?? string.Empty, "data", "screens.json");
            if (!File.Exists(path)) { _screens = new ScreenDefinition[0]; return; }
            var root = JObject.Parse(File.ReadAllText(path));
            _screens = root["screens"].Children<JObject>().Select(item => new ScreenDefinition
            {
                Id = (string)item["route"], ArabicName = (string)item["title"], Module = (string)item["module"], FeatureKey = (string)item["featureKey"], AdministratorOnly = (bool?)item["administratorOnly"] ?? false
            }).Where(item => !string.IsNullOrWhiteSpace(item.Id)).ToList();
        }
        public IReadOnlyList<ScreenDefinition> Screens { get { return _screens; } }
        public ScreenDefinition Find(string screenId) { return _screens.FirstOrDefault(item => string.Equals(item.Id, screenId, StringComparison.OrdinalIgnoreCase)); }
    }
}
