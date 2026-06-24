# Up To Date Lone's EFT DMA RADAR

# Changelog

## v3.3
- **Dynamic Exfil Status**: Real-time extraction point status read directly from game memory
  - Color-coded exfil points: Green (available), Orange (needs activation), Red (pending/unavailable)
  - Mouseover shows live status name (Available, Pending, NeedsActivation, etc.)
  - Hybrid matching: primary by settings name, fallback by proximity
  - Synced to Web Radar with `RuntimeStatus` and `StatusName` fields
- **Loot Filter Priority Fix**: Custom filter colors now correctly take precedence over Wishlist colors
- **Color Improvements**: AIPMC updated to `#ff3300` (orange-red) for better distinction from Raiders

## v3.2
- Add vertical height indicator to web radar
- Add wishlist toggle and render filtered loot with colors on web radar
- Sanitize web radar payload values and sync map ID aliases
- AIPMC classification with visual color differentiation
