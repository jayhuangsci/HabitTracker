using Microsoft.AspNetCore.Components;
using OpenSilver.MauiHybrid.Runner;

namespace HabitTracker.Mobile.MauiHybrid.Components
{
    [Route("/")]
    public class Index : ComponentBase
    {
        [Inject]
        private IMauiHybridRunner? Runner { get; set; }

        protected async override Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();

            ArgumentNullException.ThrowIfNull(Runner);
            HabitTracker.Mobile.Services.MobilePlatform.Device ??= new MauiDevice();
            await Runner.RunApplicationAsync<HabitTracker.Mobile.App>();
        }
    }
}
