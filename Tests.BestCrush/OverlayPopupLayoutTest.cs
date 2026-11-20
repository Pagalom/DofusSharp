using BestCrush.Services;

namespace Tests.BestCrush;

public sealed class OverlayPopupLayoutTest
{
    [Fact]
    public void PopupSizesSurviveClosingChangingItemAndRestarting()
    {
        string folder=Path.Combine(Path.GetTempPath(),"overlay-layout-"+Guid.NewGuid());
        string path=Path.Combine(folder,"layout.json");
        try
        {
            var settings=new OverlayLayoutSettingsService(path);
            var craft=new OverlayPopup("craft:first-guid","Craft",[]);
            var saved=new OverlayWindowLayout(40,70,335,215);
            settings.SaveLayout(craft.LayoutKind,saved);
            settings.SaveLayout(OverlayLayoutKind.PopupRunes,new(80,90,470,310));
            settings.SaveLayout(OverlayLayoutKind.Profitability,new(10,20,320,480));
            var restarted=new OverlayLayoutSettingsService(path);
            Assert.Equal(saved,restarted.GetLayout(new OverlayPopup("craft:another-guid","Craft",[]).LayoutKind));
            Assert.Equal(470,restarted.GetLayout(OverlayLayoutKind.PopupRunes).Width);
            Assert.Equal(320,restarted.GetLayout(OverlayLayoutKind.Profitability).Width);
            Assert.NotEqual(saved,restarted.GetLayout(new OverlayPopup("purchase:another-guid","Achat",[]).LayoutKind));
            Assert.True(restarted.GetLayout(OverlayLayoutKind.PopupCraft).Width<500);
        }
        finally { if(Directory.Exists(folder))Directory.Delete(folder,true); }
    }
}
