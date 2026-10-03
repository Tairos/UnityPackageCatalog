using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityPackageCatalog
{
    public sealed class ModulePackageState
    {
        public string name;
        public string version;
        public bool builtIn;
        public bool direct;
        public string[] dependencies = Array.Empty<string>();
    }

    public sealed class BuiltInModulePlan
    {
        public string[] add = Array.Empty<string>();
        public string[] remove = Array.Empty<string>();
        public string[] errors = Array.Empty<string>();
        public bool HasChanges => add.Length != 0 || remove.Length != 0;

        public static BuiltInModulePlan Create(BuiltInModuleRule[] rules, ModulePackageState[] available, ModulePackageState[] installed)
        {
            BuiltInModuleRule.Validate(rules);
            rules ??= Array.Empty<BuiltInModuleRule>();
            var known = available.Where(p => p.builtIn).ToDictionary(p => p.name, StringComparer.Ordinal);
            var current = installed.ToDictionary(p => p.name, StringComparer.Ordinal);
            var excluded = new HashSet<string>(rules.Where(r => r.requirement == "excluded").Select(r => r.name), StringComparer.Ordinal);
            var issues = new List<string>();
            var additions = new List<string>();
            var removals = new List<string>();
            var graph = new Dictionary<string, ModulePackageState>(known, StringComparer.Ordinal);
            foreach (var package in installed) graph[package.name] = package;
            foreach (var rule in rules)
            {
                if (!known.TryGetValue(rule.name, out var module))
                {
                    issues.Add(rule.name + ": module is unavailable in this Editor.");
                    continue;
                }
                if (rule.requirement == "required" && !current.ContainsKey(rule.name))
                    additions.Add(rule.name + "@" + module.version);
                if (rule.requirement == "excluded" && current.TryGetValue(rule.name, out var enabled))
                {
                    if (!enabled.builtIn) issues.Add(rule.name + ": installed source is not built-in; no change made.");
                    else if (enabled.direct) removals.Add(rule.name);
                }
                if (rule.requirement == "required" && current.TryGetValue(rule.name, out var existing) && !existing.builtIn)
                    issues.Add(rule.name + ": installed source is not built-in.");
            }
            // Keep every package except explicitly excluded modules. Required missing modules
            // also become graph roots so their dependencies are checked before enabling them.
            var roots = installed.Where(p => !excluded.Contains(p.name)).Select(p => p.name)
                .Concat(rules.Where(r => r.requirement == "required").Select(r => r.name)).Distinct();
            foreach (var root in roots)
            {
                foreach (var target in excluded)
                {
                    var path = FindPath(root, target, graph, new HashSet<string>(StringComparer.Ordinal));
                    if (path != null) issues.Add("Cannot exclude " + target + ": required by " + string.Join(" → ", path) + ".");
                }
            }
            foreach (var rule in rules.Where(r => r.requirement == "excluded"))
                if (removals.Count == 0 && current.TryGetValue(rule.name, out var package) && !package.direct &&
                    !issues.Any(issue => issue.StartsWith("Cannot exclude " + rule.name + ":", StringComparison.Ordinal)))
                    issues.Add(rule.name + ": enabled as an indirect dependency; remove its dependent packages first.");
            return new BuiltInModulePlan { add = additions.ToArray(), remove = removals.ToArray(), errors = issues.Distinct().ToArray() };
        }

        static List<string> FindPath(string name, string target, Dictionary<string, ModulePackageState> graph, HashSet<string> visited)
        {
            if (!visited.Add(name)) return null;
            if (name == target) return new List<string> { name };
            if (!graph.TryGetValue(name, out var package)) return null;
            foreach (var dependency in package.dependencies ?? Array.Empty<string>())
            {
                var path = FindPath(dependency, target, graph, visited);
                if (path == null) continue;
                path.Insert(0, name);
                return path;
            }
            return null;
        }

        public static string[] Violations(BuiltInModuleRule[] rules, ModulePackageState[] installed)
        {
            BuiltInModuleRule.Validate(rules);
            var enabled = new HashSet<string>(installed.Where(p => p.builtIn).Select(p => p.name), StringComparer.Ordinal);
            var present = new HashSet<string>(installed.Select(p => p.name), StringComparer.Ordinal);
            return (rules ?? Array.Empty<BuiltInModuleRule>()).Where(r =>
                r.requirement == "required" ? !enabled.Contains(r.name) : present.Contains(r.name))
                .Select(r => r.name + (r.requirement == "required" ? " must be enabled." : " must be disabled.")).ToArray();
        }

        public string Describe()
        {
            if (errors.Length > 0) return "Module requirements cannot be applied:\n" + string.Join("\n", errors);
            if (!HasChanges) return "All built-in module requirements are satisfied.";
            return "Enable: " + (add.Length == 0 ? "none" : string.Join(", ", add)) +
                "\nDisable: " + (remove.Length == 0 ? "none" : string.Join(", ", remove));
        }
    }
}
