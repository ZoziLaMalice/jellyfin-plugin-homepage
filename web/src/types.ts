export type CardKind = 'local' | 'request';

export interface HomeCard {
  key: string;
  kind: CardKind;
  mediaType: 'movie' | 'tv' | 'episode';
  itemId?: string;
  itemType?: string;
  detailsId?: string;
  isFolder?: boolean;
  title: string;
  subtitle?: string;
  year?: number;
  overview?: string;
  rating?: number;
  officialRating?: string;
  runTimeTicks?: number;
  genres?: string[];
  poster?: string;
  thumb?: string;
  backdrop?: string;
  logo?: string;
  tmdbId?: number;
  tvdbId?: number;
  progress?: number;
  positionTicks?: number;
  played?: boolean;
  badge?: string;
  seerrStatus?: number;
  requestSeasons?: number[];
  date?: string;
}

export interface SectionRow {
  title: string;
  display: 'Poster' | 'Landscape';
  moreLink?: string;
  items: HomeCard[];
}

export interface SectionResult {
  rows: SectionRow[];
  error?: string;
}

export interface PagedCards {
  page: number;
  totalPages: number;
  items: HomeCard[];
}

export interface SearchResult {
  library: HomeCard[];
  seerr: HomeCard[];
  seerrError?: string;
}

export interface SeasonInfo {
  number: number;
  name: string;
  episodeCount: number;
  status: number;
}

export interface MediaDetails {
  mediaType: 'movie' | 'tv';
  tmdbId: number;
  title: string;
  tagline?: string;
  overview?: string;
  year?: number;
  rating?: number;
  runtime?: number;
  genres: string[];
  poster?: string;
  backdrop?: string;
  seerrStatus?: number;
  localItemId?: string;
  seerrLink?: string;
  trailerUrl?: string;
  seasons: SeasonInfo[];
  cast: string[];
  director?: string;
  canRequest: boolean;
  cannotRequestReason?: string;
}

export interface RequestOutcome {
  success: boolean;
  message: string;
  seerrStatus?: number;
}

export interface ClientTab {
  type: 'Home' | 'Favorites' | 'Discover' | 'DiscoverMovies' | 'DiscoverTv' | 'Search' | 'Requests' | 'Library' | 'Link';
  title: string;
  target?: string;
}

export interface ClientSection {
  id: string;
  type: string;
  title: string;
  display: 'Poster' | 'Landscape';
  enabled: boolean;
  userCanHide: boolean;
}

export interface ClientLayout {
  enabled: boolean;
  canToggle: boolean;
  canCustomize: boolean;
  isAdmin: boolean;
  generation: number;
  seerrConfigured: boolean;
  sonarrConfigured: boolean;
  confirmRequests: boolean;
  header: { enabled: boolean; replaceSearch: boolean; hideNativeLinks: boolean; tabs: ClientTab[] };
  hero: { enabled: boolean; intervalSeconds: number };
  sections: ClientSection[];
  discoverRows: { id: string; page: 'Discover' | 'Movies' | 'Tv'; title: string }[];
}

export interface UserSettings {
  enabled?: boolean | null;
  heroEnabled?: boolean | null;
  headerEnabled?: boolean | null;
  sections: { id: string; enabled: boolean }[];
  excludedLibraryIds?: string[];
}

export interface LibraryChoice {
  id: string;
  name: string;
  type: 'movies' | 'tvshows';
  isVirtual: boolean;
}
