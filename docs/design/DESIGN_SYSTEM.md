# Design foundation

WPF-UI's existing dynamic brushes remain the semantic source for surfaces, borders,
primary/secondary/disabled text and control interaction states. Keep these dynamic
so later Light/System work can change them without recreating views.

Typography.xaml defines caption (12), section (20), and page-title (26) sizes.
Spacing.xaml defines existing surface padding, margins and radius. StudioResources
consumes these tokens without changing the baseline dark palette or density.
KeyboardFocus remains the shared focus indicator. Use readable Thai labels and wrap
forms instead of introducing horizontal page scrolling. Waveform scrolling is
intentional for zoomed audio.

This is an incremental foundation; inline palette migration and Light/System theme
support are subsequent work. No claim of complete component tokenization yet.
