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

        var previewHosts = new List<(ContainerModel Model, Border Host)>();
        void RefreshHomePreviews()
        {
            foreach (var (model, host) in previewHosts)
            {
                host.Child = PreviewCard.Create(
                    DesktopLook(model),
                    model.Name,
                    model.Apps.Count + " 个",
                    model.Apps,
                    220,
                    148);
            }
        }

        page.Children.Add(UnifiedOpacityCard(RefreshHomePreviews));

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
                var host = new Border
                {
                    Child = PreviewCard.Create(DesktopLook(container), container.Name, container.Apps.Count + " 个", container.Apps, 220, 148),
                    Margin = new Thickness(0, 0, 14, 14),
                    Cursor = Cursors.Hand,
                    CornerRadius = new CornerRadius(22)
                };
                previewHosts.Add((container, host));
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
                _draft = AppHost.State.ThemeAppearance(id).Clone();
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

    static Appearance DesktopLook(ContainerModel container)
    {
        if (!AppHost.State.Settings.UnifyOpacity) return container.Appearance;
        var look = container.Appearance.Clone();
        look.BackgroundOpacity = AppHost.State.Settings.UnifiedOpacity;
        return look;
    }

    UIElement UnifiedOpacityCard(Action refreshHome)
    {
        var settings = AppHost.State.Settings;
        var body = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = UiKit.Text("统一透明度", 16, FontWeights.SemiBold, Paint.Ink);
        title.VerticalAlignment = VerticalAlignment.Center;
        var mark = new TextBlock
        {
            Text = "✓",
            FontFamily = UiKit.Font,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -1, 0, 0),
            IsHitTestVisible = false
        };
        var box = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1.5),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Child = mark
        };
        var (row, slider) = UiKit.SliderRow("", 0, 1, settings.UnifiedOpacity, true);
        row.Margin = new Thickness(0, 12, 0, 0);
        var applying = false;

        void PaintBox()
        {
            var on = settings.UnifyOpacity;
            box.Background = Paint.Brush(on ? Paint.Accent : Colors.White);
            box.BorderBrush = Paint.Brush(on ? Paint.Accent : Color.FromRgb(0xE0, 0xD0, 0xD8));
            mark.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            slider.IsEnabled = on;
            row.Opacity = on ? 1 : 0.45;
        }

        void Apply(bool enabled)
        {
            if (applying) return;
            applying = true;
            settings.UnifyOpacity = enabled;
            settings.UnifiedOpacity = slider.Value;
            PaintBox();
            applying = false;
            AppHost.State.RequestSave();
            AppHost.RepaintContainers();
            refreshHome();
        }

        box.MouseLeftButtonUp += (_, _) => Apply(!settings.UnifyOpacity);
        slider.ValueChanged += (_, _) =>
        {
            if (applying || !settings.UnifyOpacity) return;
            Apply(true);
        };
        PaintBox();
        Grid.SetColumn(box, 1);
        head.Children.Add(title);
        head.Children.Add(box);
        body.Children.Add(head);
        body.Children.Add(row);
        var card = UiKit.Card(body, new Thickness(18, 16, 18, 16));
        card.Margin = new Thickness(0, 0, 0, 22);
        return card;
    }

    UIElement BuildContainers()
    {
        var containers = AppHost.State.Document.Containers;
        var root = new Grid { Margin = new Thickness(28, 22, 16, 22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = UiKit.Header("容器", UiKit.Button("新建", CreateContainer, ButtonKind.Primary));
        root.Children.Add(header);

        if (containers.Count == 0)
        {
            var empty = UiKit.Text("还没有容器", 15, FontWeights.Normal, Paint.Muted);
            Grid.SetRow(empty, 1);
            root.Children.Add(empty);
            return root;
        }

        var selected = containers.FirstOrDefault(item => item.Id == _focusContainerId) ?? containers[0];
        _focusContainerId = selected.Id;

        var body = new Grid();
        Grid.SetRow(body, 1);
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(248) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        body.ColumnDefinitions.Add(new ColumnDefinition());

        var list = new StackPanel();
        foreach (var container in containers)
            list.Children.Add(ContainerPick(container, container.Id == selected.Id));
        var listScroll = new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        body.Children.Add(listScroll);

        var detailScroll = new ScrollViewer
        {
            Content = ContainerDetail(selected, containers),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 12, 8)
        };
        Grid.SetColumn(detailScroll, 2);
        body.Children.Add(detailScroll);
        root.Children.Add(body);
        return root;
    }

    UIElement ContainerPick(ContainerModel container, bool selected)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var mark = new Border
        {
            Width = 4,
            Height = 28,
            CornerRadius = new CornerRadius(2),
            Background = Paint.Brush(selected ? Paint.Accent : Colors.Transparent),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        var text = new StackPanel { Margin = new Thickness(12, 10, 12, 10) };
        text.Children.Add(UiKit.Text(container.Name, 14, selected ? FontWeights.SemiBold : FontWeights.Normal, Paint.Ink));
        text.Children.Add(UiKit.Text(container.Apps.Count + " 个", 12, FontWeights.Normal, Paint.Muted));
        Grid.SetColumn(text, 1);
        row.Children.Add(mark);
        row.Children.Add(text);
        var card = new Border
        {
            Child = row,
            CornerRadius = new CornerRadius(12),
            Background = Paint.Brush(selected ? Color.FromRgb(0xFF, 0xE4, 0xF1) : Colors.Transparent),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 4)
        };
        var id = container.Id;
        card.MouseLeftButtonUp += (_, _) =>
        {
            if (_focusContainerId == id) return;
            _focusContainerId = id;
            AppHost.FindWindow(id)?.Nudge();
            Navigate("containers");
        };
        return card;
    }

    UIElement ContainerDetail(ContainerModel container, System.Collections.ObjectModel.ObservableCollection<ContainerModel> containers)
    {
        var page = new StackPanel();
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        var preview = PreviewCard.Create(container.Appearance, container.Name, container.Apps.Count + " 个", container.Apps, 200, 140);
        preview.VerticalAlignment = VerticalAlignment.Top;
        var previewHost = new Border { Child = preview, VerticalAlignment = VerticalAlignment.Top };
        var info = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        info.Children.Add(UiKit.Text(container.Name, 22, FontWeights.SemiBold, Paint.Ink));
        info.Children.Add(UiKit.Text(container.Apps.Count + " 个", 13, FontWeights.Normal, Paint.Muted));
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
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
        actions.Children.Add(Small("删除", () => DeleteContainer(container), true));
        info.Children.Add(actions);
        top.Children.Add(previewHost);
        Grid.SetColumn(info, 1);
        top.Children.Add(info);
        page.Children.Add(UiKit.Card(top, new Thickness(16)));

        var options = new StackPanel();
        options.Children.Add(SizeChoices("图标", container.IconSize, size =>
        {
            container.IconSize = size;
            container.UpdatedUtc = DateTime.UtcNow;
            AppHost.State.RequestSave();
        }));
        options.Children.Add(UiKit.ToggleRow("显示名称", container.ShowNames, on =>
        {
            container.ShowNames = on;
            AppHost.State.Flush();
        }));
        options.Children.Add(UiKit.ToggleRow("显示标题", container.ShowTitle, on =>
        {
            container.ShowTitle = on;
            AppHost.State.Flush();
        }));
        options.Children.Add(UiKit.ToggleRow("锁定", container.Locked, on =>
        {
            container.Locked = on;
            AppHost.State.Flush();
        }));
        var places = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        places.Children.Add(PlaceButton(container, "上", TitlePlacement.Top));
        places.Children.Add(PlaceButton(container, "下", TitlePlacement.Bottom));
        places.Children.Add(PlaceButton(container, "左上", TitlePlacement.TopLeft));
        places.Children.Add(PlaceButton(container, "居中", TitlePlacement.Center));
        options.Children.Add(places);
        AppendAppearanceEditors(options, container.Appearance, () =>
        {
            container.UpdatedUtc = DateTime.UtcNow;
            AppHost.State.RequestSave();
            previewHost.Child = PreviewCard.Create(container.Appearance, container.Name, container.Apps.Count + " 个", container.Apps, 200, 140);
        });
        var optionCard = UiKit.Card(options, new Thickness(16, 12, 16, 12));
        optionCard.Margin = new Thickness(0, 12, 0, 0);
        page.Children.Add(optionCard);

        var apps = new StackPanel();
        apps.Children.Add(UiKit.Text("里面的应用", 15, FontWeights.SemiBold, Paint.Ink));
        if (container.Apps.Count == 0)
        {
            var empty = UiKit.Text("还是空的", 13, FontWeights.Normal, Paint.Muted);
            empty.Margin = new Thickness(0, 10, 0, 0);
            apps.Children.Add(empty);
        }
        else
        {
            var appIndex = 0;
            foreach (var app in container.Apps.ToList())
            {
                var line = new Grid { Margin = new Thickness(0, 8, 0, 0) };
                line.ColumnDefinitions.Add(new ColumnDefinition());
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var label = UiKit.Text(app.Missing ? app.DisplayName + "  找不到" : app.DisplayName, 13, FontWeights.Normal, app.Missing ? Paint.Muted : Paint.Ink);
                label.VerticalAlignment = VerticalAlignment.Center;
                var ops = new StackPanel { Orientation = Orientation.Horizontal };
                var at = appIndex;
                if (!app.Missing && !container.Locked)
                {
                    ops.Children.Add(Small("上移", () =>
                    {
                        AppHost.State.ShiftApp(container, app, -1);
                        Navigate("containers");
                    }, at > 0));
                    ops.Children.Add(Small("下移", () =>
                    {
                        AppHost.State.ShiftApp(container, app, 1);
                        Navigate("containers");
                    }, at < container.Apps.Count - 1));
                }
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
                if (containers.Count > 1 && !container.Locked && !app.Missing)
                    ops.Children.Add(MoveButton(container, app));
                Grid.SetColumn(ops, 1);
                line.Children.Add(label);
                line.Children.Add(ops);
                apps.Children.Add(line);
                appIndex++;
            }
        }
        var appCard = UiKit.Card(apps, new Thickness(16));
        appCard.Margin = new Thickness(0, 12, 0, 0);
        page.Children.Add(appCard);
        return page;
    }

    UIElement BuildThemes()
    {
        _draft = AppHost.State.ThemeAppearance(_selectedThemeId).Clone();
        _themeFrames.Clear();
        var root = new Grid { Margin = new Thickness(28, 18, 28, 18) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        root.ColumnDefinitions.Add(new ColumnDefinition());

        var left = new StackPanel();
        left.Children.Add(Section("预设"));
        left.Children.Add(ThemeWrap(ThemeCatalog.BuiltIns));
        left.Children.Add(MineBlock());

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
            RememberThemeDraft();
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

        var applyRow = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        applyRow.Children.Add(Pad(UiKit.Button("设为默认", () =>
        {
            if (!string.IsNullOrWhiteSpace(_selectedThemeId))
                AppHost.State.SetDefaultTheme(_selectedThemeId);
        }, ButtonKind.Soft)));
        applyRow.Children.Add(Pad(UiKit.Button("用这个新建", CreateFromDraft, ButtonKind.Soft)));
        _updateThemeButton = UiKit.Button("更新这个主题", () =>
        {
            if (string.IsNullOrWhiteSpace(_selectedThemeId)) return;
            if (AppHost.State.Document.CustomThemes.All(theme => theme.Id != _selectedThemeId)) return;
            AppHost.State.UpdateCustomTheme(_selectedThemeId, _draft);
            Navigate("themes");
        }, ButtonKind.Soft);
        applyRow.Children.Add(Pad(_updateThemeButton));
        right.Children.Add(applyRow);
        PaintThemeFrames();

        var leftScroll = new ScrollViewer
        {
            Content = left,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 6, 12)
        };
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
        card.Children.Add(NewIconSizeRow());
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
                BorderThickness = new Thickness(2),
                BorderBrush = Paint.Brush(selected ? Paint.Accent : Color.FromArgb(0, 0, 0, 0)),
                Cursor = Cursors.Hand,
                Child = PreviewCard.Create(state.ThemeAppearance(theme.Id), theme.Name, selected ? "默认" : "", null, 140, 90)
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
        files.Children.Add(Pad(UiKit.Button("导出备份", Backup, ButtonKind.Soft)));
        files.Children.Add(Pad(UiKit.Button("导入备份", Restore, ButtonKind.Soft)));
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
        var created = AppHost.State.CreateContainer(name, _selectedThemeId, _draft, true);
        _focusContainerId = created.Id;
        Navigate("containers");
    }

    void ApplySwatch(string hex)
    {
        var color = Paint.Hex(hex);
        _draft.Background1 = Paint.ToHex(color);
        _draft.Background2 = Paint.ToHex(Paint.MixWhite(color, 0.72));
        _draft.BorderColor = Paint.ToHex(Paint.MixWhite(color, 0.35));
        _draft.TitleColor = Paint.ToHex(Paint.Darken(color, 0.45));
        PushDraft();
        RememberThemeDraft();
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

    void RememberThemeDraft()
    {
        if (string.IsNullOrWhiteSpace(_selectedThemeId)) return;
        AppHost.State.SaveThemeEdit(_selectedThemeId, _draft);
        if (_themeFrames.TryGetValue(_selectedThemeId, out var frame))
        {
            var theme = ThemeCatalog.Find(_selectedThemeId, AppHost.State.Document.CustomThemes);
            frame.Child = ThemeThumb(_draft, theme?.Name ?? "自定义", theme?.BuiltIn != false ? "" : "我的");
        }
        UpdatePreview();
    }

    void ChooseTheme(ThemeDefinition item)
    {
        _selectedThemeId = item.Id;
        _draft = AppHost.State.ThemeAppearance(item.Id).Clone();
        PaintThemeFrames();
        PushDraft();
    }

    void PaintThemeFrames()
    {
        foreach (var (id, frame) in _themeFrames)
            frame.BorderBrush = Paint.Brush(id == _selectedThemeId ? Paint.Accent : Color.FromArgb(0, 0, 0, 0));
        if (_updateThemeButton != null)
        {
            var custom = AppHost.State.Document.CustomThemes.Any(theme => theme.Id == _selectedThemeId);
            _updateThemeButton.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    static Border ThemeThumb(Appearance appearance, string title, string subtitle) =>
        PreviewCard.Create(appearance, title, subtitle, null, 132, 86);

    void UpdatePreview()
    {
        if (_previewHost == null) return;
        var theme = ThemeCatalog.Find(_selectedThemeId, AppHost.State.Document.CustomThemes);
        _previewHost.Child = PreviewCard.Create(_draft, "预览", theme?.Name ?? "自定义", null, 280, 150);
    }

    UIElement ThemeWrap(IEnumerable<ThemeDefinition> themes)
    {
        var grid = new UniformGrid { Columns = 2 };
        foreach (var theme in themes)
        {
            var item = theme;
            var frame = new Border
            {
            Margin = new Thickness(4),
            Padding = new Thickness(3),
            HorizontalAlignment = HorizontalAlignment.Center,
            CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand,
                Child = ThemeThumb(AppHost.State.ThemeAppearance(item.Id), item.Name, item.BuiltIn ? "" : "我的")
            };
            frame.MouseLeftButtonUp += (_, _) => ChooseTheme(item);
            _themeFrames[item.Id] = frame;
            if (item.BuiltIn)
            {
                grid.Children.Add(frame);
                continue;
            }
            var stack = new StackPanel();
            stack.Children.Add(frame);
            stack.Children.Add(Small("删除", () =>
            {
                if (!MiniDialog.Confirm(this, "删除「" + item.Name + "」", "已经用过它的容器不会变。", "删除"))
                    return;
                AppHost.State.DeleteCustomTheme(item.Id);
                SettingsAfterThemeDeleted(item.Id);
                Navigate("themes");
            }, true));
            grid.Children.Add(stack);
        }
        PaintThemeFrames();
        return grid;
    }

    void SettingsAfterThemeDeleted(string id)
    {
        if (_selectedThemeId != id) return;
        _selectedThemeId = ThemeCatalog.DefaultId;
        _draft = AppHost.State.ThemeAppearance(ThemeCatalog.DefaultId).Clone();
    }

    void AppendAppearanceEditors(Panel parent, Appearance appearance, Action changed)
    {
        var loading = false;
        void Edit(Action change)
        {
            if (loading) return;
            change();
            changed();
        }

        var swatches = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        foreach (var theme in ThemeCatalog.BuiltIns)
        {
            var hex = theme.Appearance.Background1;
            var dot = new Border
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(11),
                Background = Paint.Brush(Paint.Hex(hex)),
                BorderBrush = Paint.Brush(Colors.White),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            dot.MouseLeftButtonUp += (_, _) =>
            {
                var color = Paint.Hex(hex);
                loading = true;
                appearance.Background1 = Paint.ToHex(color);
                appearance.Background2 = Paint.ToHex(Paint.MixWhite(color, 0.72));
                appearance.BorderColor = Paint.ToHex(Paint.MixWhite(color, 0.35));
                appearance.TitleColor = Paint.ToHex(Paint.Darken(color, 0.45));
                loading = false;
                changed();
                Navigate("containers");
            };
            swatches.Children.Add(dot);
        }
        parent.Children.Add(swatches);

        parent.Children.Add(LocalColor("背景", () => appearance.Background1, value => Edit(() => appearance.Background1 = value)));
        parent.Children.Add(LocalColor("背景 2", () => appearance.Background2, value => Edit(() => appearance.Background2 = value)));
        parent.Children.Add(LocalColor("第三色", () => appearance.Background3 ?? "", value => Edit(() => appearance.Background3 = string.IsNullOrWhiteSpace(value) ? null : value)));
        parent.Children.Add(LocalColor("边框", () => appearance.BorderColor, value => Edit(() => appearance.BorderColor = value)));
        parent.Children.Add(LocalColor("标题", () => appearance.TitleColor, value => Edit(() => appearance.TitleColor = value)));
        parent.Children.Add(LocalSlider("透明度", 0, 1, appearance.BackgroundOpacity, value => Edit(() => appearance.BackgroundOpacity = value), true));
        parent.Children.Add(LocalSlider("边框透", 0, 1, appearance.BorderOpacity, value => Edit(() => appearance.BorderOpacity = value), true));
        parent.Children.Add(LocalSlider("圆角", 8, 40, appearance.CornerRadius, value => Edit(() => appearance.CornerRadius = value), false));
        parent.Children.Add(LocalSlider("标题字", 12, 28, appearance.TitleSize, value => Edit(() => appearance.TitleSize = value), false));
        parent.Children.Add(LocalSlider("内边距", 8, 32, appearance.Padding, value => Edit(() => appearance.Padding = value), false));
        parent.Children.Add(LocalSlider("图标距", 2, 28, appearance.IconGap, value => Edit(() => appearance.IconGap = value), false));
        var shadow = new ToggleSwitch(appearance.Shadow, on => Edit(() => appearance.Shadow = on));
        var row = new Grid { Margin = new Thickness(0, 8, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = UiKit.Text("阴影", 13, FontWeights.Normal, Paint.Ink);
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(shadow, 1);
        row.Children.Add(label);
        row.Children.Add(shadow);
        parent.Children.Add(row);
    }

    UIElement LocalColor(string label, Func<string> get, Action<string> set)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var caption = UiKit.Text(label, 13, FontWeights.Normal, Paint.Ink);
        caption.VerticalAlignment = VerticalAlignment.Center;
        var (chrome, input) = UiKit.Field(get());
        input.TextChanged += (_, _) =>
        {
            var text = input.Text.Trim();
            if (label == "第三色" && text.Length == 0)
            {
                set("");
                return;
            }
            if (Paint.TryHex(text) == null) return;
            if (!text.StartsWith('#')) text = "#" + text;
            if (string.Equals(text, get(), StringComparison.OrdinalIgnoreCase)) return;
            set(text);
        };
        Grid.SetColumn(chrome, 1);
        grid.Children.Add(caption);
        grid.Children.Add(chrome);
        return grid;
    }

    UIElement LocalSlider(string label, double min, double max, double value, Action<double> set, bool fine)
    {
        var (row, slider) = UiKit.SliderRow(label, min, max, value, fine);
        slider.ValueChanged += (_, _) =>
        {
            if (Math.Abs(slider.Value - value) < (fine ? 0.0001 : 0.01) && slider.Value == value) return;
            set(slider.Value);
        };
        return row;
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
                RememberThemeDraft();
                return;
            }
            if (Paint.TryHex(text) == null) return;
            if (!text.StartsWith('#')) text = "#" + text;
            set(text);
            RememberThemeDraft();
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
            RememberThemeDraft();
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

    UIElement MineBlock()
    {
        var body = new StackPanel();
        body.Children.Add(UiKit.Text("我的", 16, FontWeights.SemiBold, Paint.Ink));
        if (AppHost.State.Document.CustomThemes.Count == 0)
        {
            var empty = UiKit.Text("还没有自己的主题", 13, FontWeights.Normal, Paint.Muted);
            empty.Margin = new Thickness(0, 10, 0, 0);
            body.Children.Add(empty);
        }
        else
        {
            var wrap = ThemeWrap(AppHost.State.Document.CustomThemes);
            if (wrap is FrameworkElement element)
                element.Margin = new Thickness(-4, 8, -4, 0);
            body.Children.Add(wrap);
        }
        var save = UiKit.Button("存成主题", SaveTheme, ButtonKind.Primary);
        save.HorizontalAlignment = HorizontalAlignment.Left;
        save.Margin = new Thickness(0, 16, 0, 0);
        body.Children.Add(save);
        var card = UiKit.Card(body, new Thickness(16, 14, 16, 16));
        card.Margin = new Thickness(4, 22, 8, 8);
        return card;
    }

    UIElement NewIconSizeRow()
    {
        var state = AppHost.State;
        return SizeChoices("新容器图标", state.Settings.DefaultIconSize, size =>
        {
            state.Settings.DefaultIconSize = size;
            state.RequestSave();
        });
    }

    UIElement SizeChoices(string label, double current, Action<double> pick)
    {
        var options = new (string Label, double Size)[] { ("小", 40), ("中", 56), ("大", 80) };
        var selected = options.OrderBy(option => Math.Abs(current - option.Size)).First().Size;
        var buttons = new Dictionary<double, Button>();
        void MarkChosen()
        {
            foreach (var (size, button) in buttons)
                button.BorderBrush = Paint.Brush(Math.Abs(size - selected) < 0.5 ? Paint.Accent : Paint.Line);
        }

        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var caption = UiKit.Text(label, 14, FontWeights.Normal, Paint.Ink);
        caption.VerticalAlignment = VerticalAlignment.Center;
        var picks = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (text, size) in options)
        {
            var chosen = size;
            var button = Small(text, () =>
            {
                selected = chosen;
                MarkChosen();
                pick(chosen);
            }, true);
            button.Margin = new Thickness(0, 0, text == "大" ? 0 : 8, 0);
            buttons[chosen] = button;
            picks.Children.Add(button);
        }
        MarkChosen();
        Grid.SetColumn(picks, 1);
        grid.Children.Add(caption);
        grid.Children.Add(picks);
        return grid;
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
            FileName = Brand.Name + "-布局.json"
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
