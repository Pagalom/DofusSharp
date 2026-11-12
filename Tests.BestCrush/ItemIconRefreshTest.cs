using System.Reflection;
using BestCrush.Components.Pure;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using DofusSharp.DofusDb.ApiClients;
using Moq;

namespace Tests.BestCrush;

public sealed class ItemIconRefreshTest
{
    [Fact]
    public async Task RepeatedAmountAndSortRendersReuseIconButAChangedIconLoadsAgain()
    {
        string folder=Path.Combine(Path.GetTempPath(),"crush-icons-"+Guid.NewGuid());
        try
        {
            var cache=new ImageCache(folder);
            await cache.StoreImageAsync("item-icon-100.png",[1,2,3]);
            await cache.StoreImageAsync("item-icon-200.png",[4,5,6]);
            var factory=new Mock<IDofusDbClientsFactory>(MockBehavior.Strict);
            var images=new Mock<IDofusDbImagesClient<long>>();
            factory.Setup(x=>x.ItemImages()).Returns(images.Object);
            var service=new ItemsService(null!,factory.Object,null!,cache,null!);
            var icon=new ItemIcon();
            const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Public;
            typeof(ItemIcon).GetProperty("ItemsService",flags)!.SetValue(icon,service);
            void Item(long? id)=>typeof(ItemIcon).GetProperty("Item",flags)!.SetValue(icon,new Equipment(1){DofusDbIconId=id});
            Task Render()=>(Task)typeof(ItemIcon).GetMethod("OnParametersSetAsync",flags)!.Invoke(icon,null)!;
            Item(100);await Render();
            for(int i=0;i<100;i++){Item(100);await Render();}
            factory.Verify(x=>x.ItemImages(),Times.Once);
            Item(200);await Render();factory.Verify(x=>x.ItemImages(),Times.Exactly(2));
            Item(null);await Render();
            Assert.Null(typeof(ItemIcon).GetField("_icon",flags)!.GetValue(icon));
            Item(100);await Render();factory.Verify(x=>x.ItemImages(),Times.Exactly(3));
        }
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
}
