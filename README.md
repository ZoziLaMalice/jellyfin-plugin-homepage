# Homepage — plugin Jellyfin

Une page d'accueil sur mesure pour Jellyfin 12.3+ qui réunit, dans un seul plugin :

- un **carrousel (hero)** en haut de l'accueil, alimenté par les bibliothèques de ton choix ;
- une **barre de navigation** dans l'en-tête : Accueil, Découvrir, Films, Séries, Favoris, Mes demandes, Recherche ;
- des **sections configurables** : Continuer à regarder / À suivre, bibliothèques mélangées (local + recommandations JellyNext), genres, sorties digitales récentes, filtres Seerr personnalisés, prochains épisodes (Sonarr), mes demandes ;
- l'**intégration Seerr** : recherche combinée, pages Découvrir, badge « dans ta médiathèque » ou « à demander », et demande Seerr au nom de l'utilisateur connecté quand on lance un titre qu'on n'a pas.

Chaque utilisateur peut activer/désactiver l'accueil, le carrousel et la barre, et masquer ou réordonner les sections (si l'administrateur l'autorise).

## Prérequis

- Jellyfin 12.3 ou plus récent
- Le plugin [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation)
- Seerr (Jellyseerr/Overseerr) pour la découverte et les demandes, Sonarr pour les prochains épisodes (facultatifs)

## Installation

Dans Jellyfin : **Tableau de bord → Plugins → Dépôts → +**, puis ajoute :

```
https://raw.githubusercontent.com/ZoziLaMalice/jellyfin-plugin-homepage/manifest/manifest.json
```

Installe **Homepage** depuis le catalogue, redémarre Jellyfin, puis ouvre **Tableau de bord → Homepage** pour renseigner Seerr et Sonarr.

## Fonctionnement

- Les éléments des bibliothèques « virtuelles » (JellyNext : recommandations, nouvelles saisons) sont reconnus par leur chemin. S'ils correspondent à un titre que tu possèdes, la copie locale est affichée ; sinon, ils deviennent des cartes « à demander ».
- Les appels à Seerr passent par le serveur Jellyfin avec la clé API et l'en-tête `X-Api-User` : Seerr applique les droits et quotas de l'utilisateur. Le compte Seerr est trouvé via le lien Jellyfin de Seerr, le nom d'utilisateur, ou une correspondance manuelle dans la configuration.
- Seuls des chemins de lecture (`discover/*`, `search`) sont accessibles via le proxy de découverte.

## Développement

```
cd web && npm ci && npm run build    # client (TypeScript + Preact) → web/dist
dotnet publish src/Jellyfin.Plugin.Homepage -c Release
```

Chaque push sur `main` construit le plugin, publie une release et met à jour le manifeste (branche `manifest`).
