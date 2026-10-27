/** Server configuration as Jellyfin serializes it (PascalCase). */

export interface UserMapping {
  JellyfinUserId: string;
  SeerrUserId: number;
}

export interface HeaderTab {
  Type: string;
  Title: string;
  Enabled: boolean;
  Target: string;
}

export interface HeroConfig {
  Enabled: boolean;
  LibraryIds: string[];
  IncludeMovies: boolean;
  IncludeSeries: boolean;
  Count: number;
  Sort: string;
  IntervalSeconds: number;
  HideWatched: boolean;
  RequireLogo: boolean;
  IncludeSeerrTrending: boolean;
}

export interface SectionConfig {
  Id: string;
  Type: string;
  Title: string;
  Enabled: boolean;
  UserCanHide: boolean;
  Display: string;
  Limit: number;
  LibraryIds: string[];
  ItemTypes: string;
  Sort: string;
  Interleave: boolean;
  HideWatched: boolean;
  IncludeNextUp: boolean;
  Genres: string;
  GenreRows: number;
  SeerrPath: string;
  SeerrQuery: string;
  HideAvailable: boolean;
  DaysBack: number;
  DaysAhead: number;
  OnlyWatched: boolean;
}

export interface DiscoverRowConfig {
  Id: string;
  Page: string;
  Title: string;
  Enabled: boolean;
  SeerrPath: string;
  SeerrQuery: string;
  MediaType: string;
}

export interface PluginConfig {
  Initialized: boolean;
  EnabledByDefault: boolean;
  AllowUserToggle: boolean;
  AllowUserSections: boolean;
  DisabledUserIds: string[];
  VirtualPathMarkers: string;
  AssetBaseUrl: string;
  SeerrUrl: string;
  SeerrPublicUrl: string;
  SeerrApiKey: string;
  ConfirmRequests: boolean;
  UserMappings: UserMapping[];
  Region: string;
  Language: string;
  SonarrUrl: string;
  SonarrApiKey: string;
  Header: { Enabled: boolean; ReplaceSearch: boolean; Tabs: HeaderTab[] };
  Hero: HeroConfig;
  Sections: SectionConfig[];
  DiscoverRows: DiscoverRowConfig[];
}

export interface LibraryInfo {
  id: string;
  name: string;
  type: 'movies' | 'tvshows';
  isVirtual: boolean;
}

export interface JellyfinUser {
  Id: string;
  Name: string;
}
