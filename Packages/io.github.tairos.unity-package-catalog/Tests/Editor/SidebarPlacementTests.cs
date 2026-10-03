using NUnit.Framework;
using UnityEngine.UIElements;

namespace UnityPackageCatalog.Tests
{
    public class SidebarPlacementTests
    {
        public sealed class SidebarRow : VisualElement
        {
            public string pageId { get; set; }
        }

        [Test]
        public void RecreatedExtensionRowKeepsOneSourceAndOtherCloudRows()
        {
            var sidebar = new VisualElement();
            var sources = new Foldout { text = "Sources" };
            var cloud = new Foldout { text = "Cloud" };
            sidebar.Add(sources); sidebar.Add(cloud);
            var stale = new SidebarRow { pageId = "Extension/catalog" };
            var current = new SidebarRow { pageId = stale.pageId };
            var services = new SidebarRow { pageId = "Extension/services" };
            sources.Add(stale); cloud.Add(current); cloud.Add(services);
            NativeCatalogBridge.PlaceSourceRow(sidebar, current, sources, current.pageId);
            Assert.That(stale.parent, Is.Null);
            Assert.That(current.GetFirstAncestorOfType<Foldout>(), Is.SameAs(sources));
            Assert.That(services.GetFirstAncestorOfType<Foldout>(), Is.SameAs(cloud));
            NativeCatalogBridge.PlaceSourceRow(sidebar, current, sources, current.pageId);
            Assert.That(sidebar.Query<SidebarRow>().ToList(), Has.Count.EqualTo(2));
        }
    }
}
