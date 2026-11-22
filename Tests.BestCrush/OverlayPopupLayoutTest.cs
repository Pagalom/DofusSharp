using BestCrush.Services;

namespace Tests.BestCrush;

public sealed class OverlayPopupLayoutTest
{
    [Theory]
    [InlineData(40,1920,448)]
    [InlineData(1450,1920,942)]
    [InlineData(1050,1920,542)]
    public void OpensRightWhenItFitsOtherwiseLeftWithoutOverlap(int parentX,int workWidth,int expectedX)
    {
        var parent=new OverlayWindowLayout(parentX,70,400,540);
        var popup=OverlayLayoutSettingsService.CalculatePopupPlacement(parent,new(0,0,500,420),new(0,0,workWidth,1040));
        Assert.Equal(expectedX,popup.X);Assert.Equal(70,popup.Y);
        Assert.Equal(500,popup.Width);Assert.Equal(420,popup.Height);
        Assert.True(popup.X>=parent.X+parent.Width || popup.X+popup.Width<=parent.X);
    }

    [Fact]
    public void UsesTheParentsMonitorAndKeepsPopupVisibleAboveTheTaskbar()
    {
        var parent=new OverlayWindowLayout(-450,850,400,230);
        var work=new OverlayWindowLayout(-1920,40,1920,1000);
        var popup=OverlayLayoutSettingsService.CalculatePopupPlacement(parent,new(2500,120,500,420),work);
        Assert.Equal(-958,popup.X);Assert.Equal(620,popup.Y);
        Assert.True(popup.X>=work.X && popup.X+popup.Width<=work.X+work.Width);
        Assert.True(popup.Y>=work.Y && popup.Y+popup.Height<=work.Y+work.Height);
    }

    [Fact]
    public void ReducesAnOversizedPopupToTheFreeSideInsteadOfCoveringItsParent()
    {
        var parent=new OverlayWindowLayout(350,70,400,540);
        var popup=OverlayLayoutSettingsService.CalculatePopupPlacement(parent,new(0,0,900,1200),new(0,0,1200,800));
        Assert.Equal(758,popup.X);Assert.Equal(442,popup.Width);Assert.Equal(800,popup.Height);Assert.Equal(0,popup.Y);
    }

    [Fact]
    public void VerySmallScreensStillKeepTheEntirePopupOnScreen()
    {
        var popup=OverlayLayoutSettingsService.CalculatePopupPlacement(new(0,0,240,180),new(900,900,500,420),new(0,0,240,180));
        Assert.Equal(new(0,0,240,180),popup);
    }



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
