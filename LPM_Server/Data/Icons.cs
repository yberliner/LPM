using CardModel;
namespace IconsData
{
    public class IconsElements {
        public decimal Id { get; set; }
        public string? Icon { get; set; }
    };
    public class IconsService {
        private List<IconsElements> BootstrapData = new List<IconsElements>( )
        {
            new IconsElements { Id= 1, Icon= "bi bi-arrow-left-circle" },
            new IconsElements { Id= 2, Icon= "bi bi-arrows-move" },
            new IconsElements { Id= 3, Icon= "bi bi-bag" },
            new IconsElements { Id= 4, Icon= "bi bi-bar-chart-line" },
            new IconsElements { Id= 5, Icon= "bi bi-basket" },
            new IconsElements { Id= 6, Icon= "bi bi-bell" },
            new IconsElements { Id= 7, Icon= "bi bi-book" },
            new IconsElements { Id= 8, Icon= "bi bi-box" },
            new IconsElements { Id= 9, Icon= "bi bi-briefcase" },
            new IconsElements { Id= 10, Icon= "bi bi-brightness-high" },
            new IconsElements { Id= 11, Icon= "bi bi-calendar" },
            new IconsElements { Id= 12, Icon= "bi bi-paint-bucket" }
        };
        public List<IconsElements> GetBootstrap() => BootstrapData;
        private List<IconsElements> RemixiconsData = new List<IconsElements>( )
        {
            new IconsElements { Id= 1, Icon= "ri-home-line" },
            new IconsElements { Id= 2, Icon= "ri-mail-line" },
            new IconsElements { Id= 3, Icon= "ri-briefcase-line" },
            new IconsElements { Id= 4, Icon= "ri-window-line" },
            new IconsElements { Id= 5, Icon= "ri-chat-2-line" },
            new IconsElements { Id= 6, Icon= "ri-chat-settings-line" },
            new IconsElements { Id= 7, Icon= "ri-edit-line" },
            new IconsElements { Id= 8, Icon= "ri-layout-line" },
            new IconsElements { Id= 9, Icon= "ri-code-s-slash-line" },
            new IconsElements { Id= 10, Icon= "ri-airplay-line" },
            new IconsElements { Id= 11, Icon= "ri-file-line" }
        };
        public List<IconsElements> GetRemixicons() => RemixiconsData;
        private List<IconsElements> FeatherData = new List<IconsElements>( )
        {
            new IconsElements { Id= 1, Icon= "fe fe-activity" },
            new IconsElements { Id= 2, Icon= "fe fe-airplay" },
            new IconsElements { Id= 3, Icon= "fe fe-alert-circle" },
            new IconsElements { Id= 4, Icon= "fe fe-alert-triangle" },
            new IconsElements { Id= 5, Icon= "fe fe-bar-chart-2" },
            new IconsElements { Id= 6, Icon= "fe fe-bell" },
            new IconsElements { Id= 7, Icon= "fe fe-camera" },
            new IconsElements { Id= 8, Icon= "fe fe-copy" },
            new IconsElements { Id= 9, Icon= "fe fe-eye" },
            new IconsElements { Id= 10, Icon= "fe fe-file" },
            new IconsElements { Id= 11, Icon= "fe fe-layout" }
        };
        public List<IconsElements> GetFeather() => FeatherData;
        
        private List<IconsElements> TablerData = new List<IconsElements>( )
        {
            new IconsElements { Id= 1, Icon= "ti ti-brand-tabler" },
            new IconsElements { Id= 2, Icon= "ti ti-activity-heartbeat" },
            new IconsElements { Id= 3, Icon= "ti ti-alert-octagon" },
            new IconsElements { Id= 4, Icon= "ti ti-album" },
            new IconsElements { Id= 5, Icon= "ti ti-align-right" },
            new IconsElements { Id= 6, Icon= "ti ti-antenna-bars-5" },
            new IconsElements { Id= 7, Icon= "ti ti-armchair" },
            new IconsElements { Id= 8, Icon= "ti ti-arrow-big-right" },
            new IconsElements { Id= 9, Icon= "ti ti-arrows-shuffle-2" },
            new IconsElements { Id= 10, Icon= "ti ti-backspace" },
            new IconsElements { Id= 11, Icon= "ti ti-bell" },
            new IconsElements { Id= 12, Icon= "ti ti-color-picker" }
        };
        public List<IconsElements> GetTabler() => TablerData;
         private List<IconsElements> LineAwsomeData = new List<IconsElements>( )
        {
            new IconsElements { Id= 1, Icon= "las la-bell" },
            new IconsElements { Id= 2, Icon= "las la-exclamation-circle" },
            new IconsElements { Id= 3, Icon= "las la-exclamation-triangle" },
            new IconsElements { Id= 4, Icon= "las la-arrow-alt-circle-down" },
            new IconsElements { Id= 5, Icon= "las la-arrow-alt-circle-up" },
            new IconsElements { Id= 6, Icon= "las la-arrow-alt-circle-left" },
            new IconsElements { Id= 7, Icon= "las la-arrow-alt-circle-right" },
            new IconsElements { Id= 8, Icon= "las la-history" },
            new IconsElements { Id= 9, Icon= "las la-headphones" },
            new IconsElements { Id= 10, Icon= "las la-tv" },
            new IconsElements { Id= 11, Icon= "las la-car-sIde" },
            new IconsElements { Id= 12, Icon= "las la-envelope" },
            new IconsElements { Id= 13, Icon= "las la-edit" },
            new IconsElements { Id= 14, Icon= "las la-map" }
        };
        public List<IconsElements> GetLineAwsome() => LineAwsomeData;
        private List<IconsElements> BoxiconsData = new List<IconsElements>( )
        {
            new IconsElements { Id= 1, Icon= "bx bx-home" },
            new IconsElements { Id= 2, Icon= "bx bx-home-alt" },
            new IconsElements { Id= 3, Icon= "bx bx-box" },
            new IconsElements { Id= 4, Icon= "bx bx-medal" },
            new IconsElements { Id= 5, Icon= "bx bx-file" },
            new IconsElements { Id= 6, Icon= "bx bx-palette" },
            new IconsElements { Id= 7, Icon= "bx bx-receipt" },
            new IconsElements { Id= 8, Icon= "bx bx-table" },
            new IconsElements { Id= 9, Icon= "bx bx-bar-chart-alt" },
            new IconsElements { Id= 10, Icon= "bx bx-layer" },
            new IconsElements { Id= 11, Icon= "bx bx-map-alt" },
            new IconsElements { Id= 12, Icon= "bx bx-gift" },
            new IconsElements { Id= 13, Icon= "bx bx-file-blank" },
            new IconsElements { Id= 14, Icon= "bx bx-lock-alt" },
            new IconsElements { Id= 15, Icon= "bx bx-error" },
            new IconsElements { Id= 16, Icon= "bx bx-error-circle" }
        };
        public List<IconsElements> GetBoxicons() => BoxiconsData;
    }
}