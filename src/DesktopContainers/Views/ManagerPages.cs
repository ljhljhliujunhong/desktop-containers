using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace DesktopContainers;

public partial class ManagerWindow
{
    UIElement BuildHome()
    {
        var state = AppHost.State;
        var containers = state.Document.Containers;
        var page = UiKit.Header("我的桌面", UiKit.Button("新建", CreateContainer, ButtonKind.Primary));

        var stats = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 22) };
        stats.Children.Add(Stat(containers.Count.ToString(), "容器"));
        stats.Children.Add(Stat(containers.Sum(item => item.Apps.Count).ToString(), "应用"));
        page.Children.Add(stats);

        if (containers.Count == 0)
        {
            var empty = new StackPanel();
            empty.Children.Add(UiKit.Text("还没有容器", 16, FontWeights.SemiBold, Paint.Ink));
            var create = UiKit.Button("创建一个", CreateContainer, ButtonKind.Primary);
            create.HorizontalAlignment = HorizontalAlignment.Left;
            create.Margin = new Thickness(0, 14, 0, 0);
            empty.Children.Add(create);
            page.Children.Add(UiKit.Card(empty, new Thickness(22)));
        }
        else
        {
            page.Children.Add(Section("最近"));
            var wrap = new WrapPanel();
            foreach (var container in containers.OrderByDescending(item => item.UpdatedUtc).Take(4))
            {
                var card = PreviewCard.Create(container.Appearance, container.Name, container.Apps.Count + " 个", container.Apps, 220, 148);
                var host = new Border
                {
                    Child = card,
                    Margin = new Thickness(0, 0, 14, 14),
                    Cursor = Cursors.Hand,
                    CornerRadius = new CornerRadius(22)
                };
                var id = container.Id;
                host.MouseLeftButtonUp += (_, _) =>
                {
                    AppHost.FindWindow(id)?.Nudge();
                    _focusContainerId = id;
                    Navigate("containers");
                };
                wrap.Children.Add(host);
            }
            page.Children.Add(wrap);
        }

        page.Children.Add(Section("主题"));
        var themes = new WrapPanel();
        foreach (var theme in ThemeCatalog.BuiltIns.Take(6))
        {
            var card = PreviewCard.Create(theme.Appearance, theme.Name, "", null, 150, 96);
            var host = new Border { Child = card, Margin = new Thickness(0, 0, 12, 12), Cursor = Cursors.Hand };
            var id = theme.Id;
            var appearance = theme.Appearance;
            host.MouseLeftButtonUp += (_, _) =>
            {
                _selectedThemeId = id;
                _draft = appearance.Clone();
                Navigate("themes");
            };
            themes.Children.Add(host);
        }
        page.Children.Add(themes);
        var all = UiKit.Button("全部主题", () => Navigate("themes"), ButtonKind.Ghost);
        all.HorizontalAlignment = HorizontalAlignment.Left;
        page.Children.Add(all);
        return page;
    }

    UIElement BuildContainers()
    {
        var page = UiKit.Header("容器", UiKit.Button("新建", CreateContainer, ButtonKind.Primary));
        var containers = AppHost.State.Document.Containers;
        if (containers.Count == 0)
        {
            page.Children.Add(UiKit.Text("还没有容器", 15, FontWeights.Normal, Paint.Muted));
            return page;
        }

        foreach (var container in containers)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var preview = PreviewCard.Create(container.Appearance, container.Name, container.Apps.Count + " 个", container.Apps, 190, 132);
            preview.VerticalAlignment = VerticalAlignment.Top;
            var info = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            info.Children.Add(UiKit.Text(container.Name, 18, FontWeights.SemiBold, Paint.Ink));
            info.Children.Add(UiKit.Text(container.Apps.Count + " 个", 12, FontWeights.Normal, Paint.Muted));

            var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            var index = containers.IndexOf(container);
            actions.Children.Add(Small("上移", () => { AppHost.State.MoveContainer(container, -1); Navigate("containers"); }, index > 0));
            actions.Children.Add(Small("下移", () => { AppHost.State.MoveContainer(container, 1); Navigate("containers"); }, index < containers.Count - 1));
            actions.Children.Add(Small("重命名", () =>
            {
                var name = MiniDialog.Prompt(this, "重命名", container.Name);
                if (name == null) return;
                AppHost.State.Rename(container, name);
                Navigate("containers");
            }, true));
            actions.Children.Add(Small("外观", () => Open("themes", container.Id), true));
            actions.Children.Add(Small("删除", () => DeleteContainer(container), true));
            info.Children.Add(actions);

            var (sizeRow, sizeSlider) = UiKit.SliderRow("图标", 32, 96, container.IconSize);
            sizeSlider.ValueChanged += (_, _) =>
            {
                container.IconSize = sizeSlider.Value;
                container.UpdatedUtc = DateTime.UtcNow;
                AppHost.State.RequestSave();
            };
            info.Children.Add(sizeRow);
            info.Children.Add(UiKit.ToggleRow("显示名称", container.ShowNames, on =>
            {
                container.ShowNames = on;
                AppHost.State.Flush();
            }));
            info.Children.Add(UiKit.ToggleRow("显示标题", container.ShowTitle, on =>
            {
                container.ShowTitle = on;
                AppHost.State.Flush();
            }));
            info.Children.Add(UiKit.ToggleRow("锁定", container.Locked, on =>
            {
                container.Locked = on;
                AppHost.State.Flush();
            }));

            var places = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
            places.Children.Add(PlaceButton(container, "上", TitlePlacement.Top));
            places.Children.Add(PlaceButton(container, "下", TitlePlacement.Bottom));
            places.Children.Add(PlaceButton(container, "左上", TitlePlacement.TopLeft));
            places.Children.Add(PlaceButton(container, "居中", TitlePlacement.Center));
            info.Children.Add(places);

            if (container.Apps.Count > 0)
            {
                info.Children.Add(UiKit.Text("里面的应用", 13, FontWeights.SemiBold, Paint.Ink));
                foreach (var app in container.Apps.ToList())
                {
                    var line = new Grid { Margin = new Thickness(0, 6, 0, 0) };
                    line.ColumnDefinitions.Add(new ColumnDefinition());
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var label = UiKit.Text(app.Missing ? app.DisplayName + "  找不到" : app.DisplayName, 13, FontWeights.Normal, app.Missing ? Paint.Muted : Paint.Ink);
                    label.VerticalAlignment = VerticalAlignment.Center;
                    var ops = new StackPanel { Orientation = Orientation.Horizontal };
                    if (app.Missing)
                        ops.Children.Add(Small("移除", () => { AppHost.State.RemoveMissing(container, app); Navigate("containers"); }, true));
                    else
                        ops.Children.Add(Small("移出", () =>
                        {
                            if (!AppHost.State.RestoreApp(container, app))
                                MiniDialog.Alert(this, "没能移出", "快捷方式还在容器里。");
                            else
                                Navigate("containers");
                        }, !container.Locked));
                    if (containers.Count > 1 && !container.Locked)
                        ops.Children.Add(MoveButton(container, app));
                    Grid.SetColumn(ops, 1);
                    line.Children.Add(label);
                    line.Children.Add(ops);
                    info.Children.Add(line);
                }
            }

            row.Children.Add(preview);
            Grid.SetColumn(info, 1);
            row.Children.Add(info);
            var card = UiKit.Card(row, new Thickness(14));
            card.Margin = new Thickness(0, 0, 0, 14);
            if (container.Id == _focusContainerId)
                card.BorderBrush = Paint.Brush(Paint.Accent);
            page.Children.Add(card);
        }
        return page;
    }

    UIElement BuildThemes()
    {
        var root = new Grid { Margin = new Thickness(28, 18, 28, 18) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        root.ColumnDefinitions.Add(new ColumnDefinition());

        var left = new StackPanel();
        left.Children.Add(Section("预设"));
        left.Children.Add(ThemeWrap(ThemeCatalog.BuiltIns));
        left.Children.Add(Section("我的"));
        if (AppHost.State.Document.CustomThemes.Count == 0)
            left.Children.Add(UiKit.Text("还没有自己的主题", 13, FontWeights.Normal, Paint.Muted));
        else
            left.Children.Add(ThemeWrap(AppHost.State.Document.CustomThemes));
        var save = UiKit.Button("存成主题", SaveTheme, ButtonKind.Primary);
        save.HorizontalAlignment = HorizontalAlignment.Left;
        save.Margin = new Thickness(0, 8, 0, 0);
        left.Children.Add(save);

        var right = new StackPanel();
        _previewHost = new Border { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
        right.Children.Add(_previewHost);
        UpdatePreview();

        var swatches = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var theme in ThemeCatalog.BuiltIns)
        {
            var color = Paint.Hex(theme.Appearance.Background1);
            var dot = new Border
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(11),
                Background = Paint.Brush(color),
                Cursor = Cursors.Hand,
                BorderBrush = Paint.Brush(Colors.White),
                BorderThickness = new Thickness(1)
            };
            var hex = theme.Appearance.Background1;
            dot.MouseLeftButtonUp += (_, _) => ApplySwatch(hex);
            swatches.Children.Add(dot);
        }
        right.Children.Add(swatches);
        right.Children.Add(ColorRow("背景", _draft.Background1, value => _draft.Background1 = value, out _color1));
        right.Children.Add(ColorRow("背景 2", _draft.Background2, value => _draft.Background2 = value, out _color2));
        right.Children.Add(ColorRow("第三色", _draft.Background3 ?? "", value => _draft.Background3 = string.IsNullOrWhiteSpace(value) ? null : value, out _color3));
        right.Children.Add(ColorRow("边框", _draft.BorderColor, value => _draft.BorderColor = value, out _borderColor));
        right.Children.Add(ColorRow("标题", _draft.TitleColor, value => _draft.TitleColor = value, out _titleColor));

        right.Children.Add(BindSlider("透明度", 0, 1, _draft.BackgroundOpacity, value => _draft.BackgroundOpacity = value, out _opacitySlider, fine: true));
        right.Children.Add(BindSlider("边框透", 0, 1, _draft.BorderOpacity, value => _draft.BorderOpacity = value, out _borderOpacitySlider, fine: true));
        right.Children.Add(BindSlider("圆角", 8, 40, _draft.CornerRadius, value => _draft.CornerRadius = value, out _radiusSlider));
        right.Children.Add(BindSlider("标题字", 12, 28, _draft.TitleSize, value => _draft.TitleSize = value, out _titleSizeSlider));
        right.Children.Add(BindSlider("内边距", 8, 32, _draft.Padding, value => _draft.Padding = value, out _paddingSlider));
        right.Children.Add(BindSlider("图标距", 2, 28, _draft.IconGap, value => _draft.IconGap = value, out _gapSlider));
        _shadowSwitch = new ToggleSwitch(_draft.Shadow, on =>
        {
            if (_loadingDraft) return;
            _draft.Shadow = on;
            UpdatePreview();
            LiveApply();
        });
        var shadowRow = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        shadowRow.ColumnDefinitions.Add(new ColumnDefinition());
        shadowRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var shadowLabel = UiKit.Text("阴影", 13, FontWeights.Normal, Paint.Ink);
        shadowLabel.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_shadowSwitch, 1);
        shadowRow.Children.Add(shadowLabel);
        shadowRow.Children.Add(_shadowSwitch);
        right.Children.Add(shadowRow);

        right.Children.Add(Section("用在"));
        _containerChecks.Clear();
        if (AppHost.State.Document.Containers.Count == 0)
        {
            right.Children.Add(UiKit.Text("还没有容器", 13, FontWeights.Normal, Paint.Muted));
        }
        else
        {
            var checks = new WrapPanel();
            foreach (var container in AppHost.State.Document.Containers)
            {
                var id = container.Id;
                var box = new CheckBox
                {
                    Content = container.Name,
                    Tag = id,
                    IsChecked = _checked.Contains(id),
                    FontFamily = UiKit.Font,
                    Foreground = Paint.Brush(Paint.Ink),
                    Margin = new Thickness(0, 4, 16, 4),
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                box.Checked += (_, _) =>
                {
                    if (_suppressCheckEvents) return;
                    _checked.Add(id);
                };
                box.Unchecked += (_, _) =>
                {
                    if (_suppressCheckEvents) return;
                    _checked.Remove(id);
                    if (_focusContainerId == id) _focusContainerId = null;
                };
                _containerChecks.Add(box);
                checks.Children.Add(box);
            }
            right.Children.Add(checks);
        }

        var applyRow = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        applyRow.Children.Add(Pad(UiKit.Button("用到勾选的容器", ApplyChecked, ButtonKind.Primary)));
        applyRow.Children.Add(Pad(UiKit.Button("设为默认", () =>
        {
            if (!string.IsNullOrWhiteSpace(_selectedThemeId))
                AppHost.State.SetDefaultTheme(_selectedThemeId);
        }, ButtonKind.Soft)));
        applyRow.Children.Add(Pad(UiKit.Button("用这个新建", CreateFromDraft, ButtonKind.Soft)));
        var custom = AppHost.State.Document.CustomThemes.FirstOrDefault(theme => theme.Id == _selectedThemeId);
        if (custom != null)
            applyRow.Children.Add(Pad(UiKit.Button("更新这个主题", () =>
            {
                AppHost.State.UpdateCustomTheme(custom.Id, _draft);
                Navigate("themes");
            }, ButtonKind.Soft)));
        right.Children.Add(applyRow);

        var leftScroll = new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var rightScroll = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(rightScroll, 2);
        root.Children.Add(leftScroll);
        root.Children.Add(rightScroll);
        return root;
    }

    UIElement BuildSettings()
    {
        var state = AppHost.State;
        var page = UiKit.Header("设置", null);
        var card = new StackPanel();
        card.Children.Add(UiKit.ToggleRow("开机自动运行", state.Settings.LaunchAtStartup, on =>
        {
            if (state.SetLaunchAtStartup(on)) return;
            MiniDialog.Alert(this, "没能改开机启动", "再试一次。");
            Navigate("settings");
        }));
        card.Children.Add(UiKit.ToggleRow("新容器显示标题", state.Settings.ShowTitlesByDefault, on =>
        {
            state.Settings.ShowTitlesByDefault = on;
            state.Flush();
        }));
        card.Children.Add(UiKit.ToggleRow("动画", state.Settings.AnimationsEnabled, on =>
        {
            state.Settings.AnimationsEnabled = on;
            state.Flush();
        }));
        card.Children.Add(UiKit.ToggleRow("编辑桌面", state.Settings.EditMode, AppHost.SetEditMode));
        var (sliderRow, slider) = UiKit.SliderRow("默认图标", 32, 96, state.Settings.DefaultIconSize);
        slider.ValueChanged += (_, _) =>
        {
            state.Settings.DefaultIconSize = slider.Value;
            state.RequestSave();
        };
        card.Children.Add(sliderRow);
        page.Children.Add(UiKit.Card(card));

        page.Children.Add(Section("默认主题"));
        var themes = new WrapPanel();
        foreach (var theme in ThemeCatalog.All(state.Document.CustomThemes))
        {
            var selected = theme.Id == state.Settings.DefaultThemeId;
            var frame = new Border
            {
                Padding = new Thickness(3),
                Margin = new Thickness(0, 0, 10, 10),
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(selected ? 2 : 0),
                BorderBrush = Paint.Brush(Paint.Accent),
                Cursor = Cursors.Hand,
                Child = PreviewCard.Create(theme.Appearance, theme.Name, selected ? "默认" : "", null, 140, 90)
            };
            var id = theme.Id;
            frame.MouseLeftButtonUp += (_, _) =>
            {
                state.SetDefaultTheme(id);
                Navigate("settings");
            };
            themes.Children.Add(frame);
        }
        page.Children.Add(themes);

        var files = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        files.Children.Add(Pad(UiKit.Button("备份布局", Backup, ButtonKind.Soft)));
        files.Children.Add(Pad(UiKit.Button("恢复布局", Restore, ButtonKind.Soft)));
        page.Children.Add(files);
        return page;
    }

    void DeleteContainer(ContainerModel container)
    {
        var message = container.Apps.Count == 0
            ? "删除这个容器？"
            : "能用的快捷方式会回到桌面，程序不会被卸掉。";
        if (!MiniDialog.Confirm(this, "删除「" + container.Name + "」", message, "删除"))
            return;
        var error = AppHost.State.DeleteContainer(container);
        if (error != null) MiniDialog.Alert(this, "没删掉", error);
        Navigate("containers");
    }

    void SaveTheme()
    {
        var name = MiniDialog.Prompt(this, "主题名称", "我的主题");
        if (name == null) return;
        var theme = AppHost.State.SaveCustomTheme(name, _draft);
        _selectedThemeId = theme.Id;
        Navigate("themes");
    }

    void CreateFromDraft()
    {
        var name = MiniDialog.Prompt(this, "容器名称", "新容器", "创建");
        if (name == null) return;
        AppHost.State.CreateContainer(name, _selectedThemeId, _draft, true);
        Navigate("containers");
    }

    void ApplyChecked()
    {
        var ids = TargetContainers();
        if (ids.Count == 0)
        {
            MiniDialog.Alert(this, "还没选容器", "先勾选要换外观的容器。");
            return;
        }
        foreach (var id in ids) _checked.Add(id);
        AppHost.State.ApplyAppearance(ids, _draft, _selectedThemeId);
    }

    List<string> TargetContainers()
    {
        var ids = _containerChecks
            .Where(box => box.IsChecked == true && box.Tag is string)
            .Select(box => (string)box.Tag)
            .Distinct()
            .ToList();
        if (ids.Count > 0) return ids;
        if (!string.IsNullOrWhiteSpace(_focusContainerId) && AppHost.State.Find(_focusContainerId) != null)
            return [_focusContainerId];
        var containers = AppHost.State.Document.Containers;
        if (containers.Count == 1) return [containers[0].Id];
        return [];
    }

    void ApplySwatch(string hex)
    {
        var color = Paint.Hex(hex);
        _draft.Background1 = Paint.ToHex(color);
        _draft.Background2 = Paint.ToHex(Paint.MixWhite(color, 0.72));
        _draft.BorderColor = Paint.ToHex(Paint.MixWhite(color, 0.35));
        _draft.TitleColor = Paint.ToHex(Paint.Darken(color, 0.45));
        PushDraft();
    }

    void PushDraft()
    {
        _loadingDraft = true;
        if (_color1 != null) _color1.Text = _draft.Background1;
        if (_color2 != null) _color2.Text = _draft.Background2;
        if (_color3 != null) _color3.Text = _draft.Background3 ?? "";
        if (_borderColor != null) _borderColor.Text = _draft.BorderColor;
        if (_titleColor != null) _titleColor.Text = _draft.TitleColor;
        if (_opacitySlider != null) _opacitySlider.Value = _draft.BackgroundOpacity;
        if (_borderOpacitySlider != null) _borderOpacitySlider.Value = _draft.BorderOpacity;
        if (_radiusSlider != null) _radiusSlider.Value = _draft.CornerRadius;
        if (_titleSizeSlider != null) _titleSizeSlider.Value = _draft.TitleSize;
        if (_paddingSlider != null) _paddingSlider.Value = _draft.Padding;
        if (_gapSlider != null) _gapSlider.Value = _draft.IconGap;
        _shadowSwitch?.SetSilent(_draft.Shadow);
        _loadingDraft = false;
        UpdatePreview();
    }

    void LiveApply()
    {
        var ids = TargetContainers();
        if (ids.Count == 0) return;
        AppHost.State.PreviewAppearance(ids, _draft);
    }

    void UpdatePreview()
    {
        if (_previewHost == null) return;
        var theme = ThemeCatalog.Find(_selectedThemeId, AppHost.State.Document.CustomThemes);
        _previewHost.Child = PreviewCard.Create(_draft, "预览", theme?.Name ?? "自定义", null, 280, 150);
    }

    UIElement ThemeWrap(IEnumerable<ThemeDefinition> themes)
    {
        var wrap = new WrapPanel();
        foreach (var theme in themes)
        {
            var selected = theme.Id == _selectedThemeId;
            var frame = new Border
            {
                Padding = new Thickness(3),
                Margin = new Thickness(0, 0, 10, 10),
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(selected ? 2 : 0),
                BorderBrush = Paint.Brush(Paint.Accent),
                Cursor = Cursors.Hand,
                Child = PreviewCard.Create(theme.Appearance, theme.Name, theme.BuiltIn ? "" : "我的", null, 148, 96)
            };
            var item = theme;
            frame.MouseLeftButtonUp += (_, _) =>
            {
                _selectedThemeId = item.Id;
                _draft = item.Appearance.Clone();
                Navigate("themes");
            };
            if (theme.BuiltIn)
            {
                wrap.Children.Add(frame);
                continue;
            }
            var stack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            stack.Children.Add(frame);
            stack.Children.Add(Small("删除", () =>
            {
                if (!MiniDialog.Confirm(this, "删除「" + item.Name + "」", "已经用过它的容器不会变。", "删除"))
                    return;
                AppHost.State.DeleteCustomTheme(item.Id);
                if (_selectedThemeId == item.Id)
                {
                    _selectedThemeId = ThemeCatalog.DefaultId;
                    _draft = ThemeCatalog.CreateAppearance(ThemeCatalog.DefaultId);
                }
                Navigate("themes");
            }, true));
            wrap.Children.Add(stack);
        }
        return wrap;
    }

    UIElement ColorRow(string label, string value, Action<string> set, out TextBox box)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var caption = UiKit.Text(label, 13, FontWeights.Normal, Paint.Ink);
        caption.VerticalAlignment = VerticalAlignment.Center;
        var (chrome, input) = UiKit.Field(value);
        box = input;
        input.TextChanged += (_, _) =>
        {
            if (_loadingDraft) return;
            var text = input.Text.Trim();
            if (label == "第三色" && text.Length == 0)
            {
                set("");
                UpdatePreview();
                LiveApply();
                return;
            }
            if (Paint.TryHex(text) == null) return;
            if (!text.StartsWith('#')) text = "#" + text;
            set(text);
            UpdatePreview();
            LiveApply();
        };
        Grid.SetColumn(chrome, 1);
        grid.Children.Add(caption);
        grid.Children.Add(chrome);
        return grid;
    }

    UIElement BindSlider(string label, double min, double max, double value, Action<double> set, out Slider slider, bool fine = false)
    {
        var (row, created) = UiKit.SliderRow(label, min, max, value, fine);
        slider = created;
        created.ValueChanged += (_, _) =>
        {
            if (_loadingDraft) return;
            set(created.Value);
            UpdatePreview();
            LiveApply();
        };
        return row;
    }

    Button PlaceButton(ContainerModel container, string label, TitlePlacement placement)
    {
        var selected = container.TitlePlacement == placement && container.ShowTitle;
        var button = Small(label, () =>
        {
            container.TitlePlacement = placement;
            container.ShowTitle = true;
            AppHost.State.Flush();
            Navigate("containers");
        }, true);
        if (selected) button.BorderBrush = Paint.Brush(Paint.Accent);
        return button;
    }

    Button MoveButton(ContainerModel container, AppEntry app)
    {
        Button button = null!;
        button = Small("移到", () =>
        {
            var menu = new ContextMenu();
            foreach (var other in AppHost.State.Document.Containers.Where(item => item.Id != container.Id))
            {
                var target = other;
                var item = new MenuItem { Header = target.Name };
                item.Click += (_, _) =>
                {
                    if (target.Locked) return;
                    AppHost.State.MoveApp(container.Id, app.Id, target.Id, target.Apps.Count);
                    Navigate("containers");
                };
                menu.Items.Add(item);
            }
            button.ContextMenu = menu;
            menu.IsOpen = true;
        }, true);
        return button;
    }

    static Button Small(string text, Action click, bool enabled)
    {
        var button = UiKit.Button(text, click, ButtonKind.Soft);
        button.Margin = new Thickness(0, 0, 8, 8);
        button.MinHeight = 32;
        button.Padding = new Thickness(10, 4, 10, 4);
        button.IsEnabled = enabled;
        return button;
    }

    static UIElement Pad(Button button)
    {
        button.Margin = new Thickness(0, 0, 8, 8);
        return button;
    }

    static UIElement Section(string title)
    {
        var text = UiKit.Text(title, 16, FontWeights.SemiBold, Paint.Ink);
        text.Margin = new Thickness(0, 8, 0, 10);
        return text;
    }

    static UIElement Stat(string value, string label)
    {
        var stack = new StackPanel();
        stack.Children.Add(UiKit.Text(value, 32, FontWeights.SemiBold, Paint.Ink));
        stack.Children.Add(UiKit.Text(label, 13, FontWeights.Normal, Paint.Muted));
        var card = UiKit.Card(stack, new Thickness(18, 16, 18, 16));
        card.Margin = new Thickness(0, 0, 12, 0);
        return card;
    }

    void Backup()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "布局文件|*.json",
            FileName = "桌面容器-布局.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            AppHost.State.Flush();
            File.Copy(AppHost.Store.LayoutPath, dialog.FileName, true);
        }
        catch (Exception ex)
        {
            Log.Error("backup", ex);
            MiniDialog.Alert(this, "没能备份", "换个位置再试。");
        }
    }

    void Restore()
    {
        var dialog = new OpenFileDialog { Filter = "布局文件|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        if (!MiniDialog.Confirm(this, "恢复布局", "用这个备份换掉现在的布局？", "恢复"))
            return;
        var error = AppHost.State.RestoreFrom(dialog.FileName);
        if (error != null) MiniDialog.Alert(this, "没能恢复", error);
        else Navigate("settings");
    }
}
