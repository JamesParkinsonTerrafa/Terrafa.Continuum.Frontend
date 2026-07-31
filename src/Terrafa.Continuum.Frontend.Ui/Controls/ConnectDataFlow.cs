// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls;

/// <summary>
/// Drives the CONNECT REAL DATA dialog through its three steps — pick a route, sign in, or ask for
/// an account — reusing the one <see cref="OverlayDialog"/> the screen already hosts rather than
/// stacking modals.
/// </summary>
public static class ConnectDataFlow
{
    private const string DemoSubject = "Terrafa Continuum — demo account request";

    /// <param name="onSignedIn">Runs after a successful sign-in, once the dialog has closed.</param>
    public static void Show(OverlayDialog dialog, AuthSession session, Action onSignedIn) =>
        ShowChoice(dialog, session, onSignedIn);

    // ── step 1: which route ──────────────────────────────────────────────────

    private static void ShowChoice(OverlayDialog dialog, AuthSession session, Action onSignedIn)
    {
        var chosen = "";
        Border? signInCard = null;
        Border? demoCard = null;

        signInCard = OptionCard(
            "SIGN IN",
            "Use the username and password we issued you. Your own datasets replace the demo catalogue.",
            () =>
            {
                chosen = "sign-in";
                Select(signInCard!, demoCard!);
            });

        demoCard = OptionCard(
            "REQUEST DEMO ACCOUNT",
            "Send us your name, email and company and we will set an account up for you.",
            () =>
            {
                chosen = "demo";
                Select(demoCard!, signInCard!);
            });

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock
        {
            Text = "Everything on screen right now is demo data. Connect an account to read your own "
                   + "datasets from the live catalogue.",
            FontSize = TypographySettings.Size(11),
            LineHeight = TypographySettings.Size(17),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.TextSub
        });
        body.Children.Add(signInCard);
        body.Children.Add(demoCard);
        body.Children.Add(ContactFooter());

        dialog.Show("CONNECT REAL DATA", body, "CONTINUE <GO>", () =>
        {
            switch (chosen)
            {
                case "sign-in":
                    ShowSignIn(dialog, session, onSignedIn);
                    return false;
                case "demo":
                    ShowDemoRequest(dialog);
                    return false;
                default:
                    return false;
            }
        }, width: 520);
    }

    // ── step 2: sign in ──────────────────────────────────────────────────────

    private static void ShowSignIn(OverlayDialog dialog, AuthSession session, Action onSignedIn)
    {
        var username = Field("USERNAME");
        var password = Field("PASSWORD", isPassword: true);
        var status = new TextBlock
        {
            FontSize = TypographySettings.Size(10),
            LineHeight = TypographySettings.Size(15),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.Red,
            IsVisible = false
        };

        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock
        {
            Text = "Sign in with the credentials we issued you.",
            FontSize = TypographySettings.Size(11),
            LineHeight = TypographySettings.Size(17),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.TextSub
        });
        body.Children.Add(username.Root);
        body.Children.Add(password.Root);
        body.Children.Add(status);

        if (!AuthOptions.IsConfigured || !DataFeedOptions.IsConfigured)
        {
            // Saying this up front beats letting someone type a password into a box that was never
            // going to reach anything.
            status.Text = !AuthOptions.IsConfigured
                ? "No user pool is configured in this build, so sign-in is not available yet."
                : $"No data service address is configured in this build, so there is nothing to read once signed in.";
            status.IsVisible = true;
        }

        var busy = false;

        dialog.Show("SIGN IN", body, "SIGN IN <GO>", () =>
        {
            if (busy) return false;

            var user = username.Box.Text?.Trim() ?? "";
            var pass = password.Box.Text ?? "";
            if (user.Length == 0 || pass.Length == 0)
            {
                Report(status, "Enter both a username and a password.", Palette.Red);
                return false;
            }

            busy = true;
            Report(status, "signing in…", Palette.TextMuted);
            _ = SignInAsync();
            // Always stays open: the sign-in is still in flight, and its continuation is what
            // closes the dialog or puts the failure in the status line.
            return false;

            async Task SignInAsync()
            {
                try
                {
                    await session.SignInAsync(user, pass);
                    dialog.Hide();
                    onSignedIn();
                }
                catch (AuthException ex)
                {
                    Report(status, ex.Message, Palette.Red);
                }
                catch (Exception ex)
                {
                    Report(status, $"Sign-in failed unexpectedly. {ex.Message}", Palette.Red);
                }
                finally
                {
                    busy = false;
                }
            }
        }, width: 460);
    }

    // ── step 3: request a demo account ───────────────────────────────────────

    private static void ShowDemoRequest(OverlayDialog dialog)
    {
        var name = Field("NAME");
        var email = Field("EMAIL");
        var company = Field("COMPANY");
        var status = new TextBlock
        {
            FontSize = TypographySettings.Size(10),
            LineHeight = TypographySettings.Size(15),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.Red,
            IsVisible = false
        };

        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock
        {
            Text = "Tell us who you are and we will set up an account. This opens your mail app with "
                   + $"the message ready to send to {AuthOptions.ContactEmail}.",
            FontSize = TypographySettings.Size(11),
            LineHeight = TypographySettings.Size(17),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.TextSub
        });
        body.Children.Add(name.Root);
        body.Children.Add(email.Root);
        body.Children.Add(company.Root);
        body.Children.Add(status);
        body.Children.Add(ContactFooter());

        dialog.Show("REQUEST DEMO ACCOUNT", body, "SEND REQUEST <GO>", () =>
        {
            var who = name.Box.Text?.Trim() ?? "";
            var address = email.Box.Text?.Trim() ?? "";
            var org = company.Box.Text?.Trim() ?? "";

            if (who.Length == 0 || address.Length == 0 || org.Length == 0)
            {
                Report(status, "Fill in your name, email and company.", Palette.Red);
                return false;
            }
            // Deliberately shallow: the address only has to be good enough to mail back to, and a
            // strict pattern here would reject valid addresses to no benefit.
            if (!address.Contains('@') || address.StartsWith('@') || address.EndsWith('@'))
            {
                Report(status, "That does not look like an email address.", Palette.Red);
                return false;
            }

            var launched = Launch(body, MailTo(who, address, org));
            if (!launched)
            {
                Report(status,
                    $"Could not open a mail app. Email {AuthOptions.ContactEmail} directly with your "
                    + "name, email and company.", Palette.Red);
                return false;
            }
            return true;
        }, width: 460);
    }

    private static Uri MailTo(string name, string email, string company)
    {
        var body = string.Join(
            "\r\n",
            "Please set up a Terrafa Continuum demo account.",
            "",
            $"Name: {name}",
            $"Email: {email}",
            $"Company: {company}");

        return new Uri(
            $"mailto:{AuthOptions.ContactEmail}"
            + $"?subject={Uri.EscapeDataString(DemoSubject)}"
            + $"&body={Uri.EscapeDataString(body)}");
    }

    /// <summary>
    /// Hands the URI to the platform. Avalonia's launcher is what makes this work on both heads —
    /// the desktop one shells out, the browser one opens it from the page; Process.Start would
    /// compile and then throw in wasm.
    /// </summary>
    private static bool Launch(Visual from, Uri uri)
    {
        var launcher = TopLevel.GetTopLevel(from)?.Launcher;
        if (launcher is null) return false;

        // Fire and forget: the task completes when the OS has taken the URI, which tells us
        // nothing about whether a mail app actually opened, and blocking the dialog on it would
        // just freeze the button.
        _ = launcher.LaunchUriAsync(uri);
        return true;
    }

    // ── shared pieces ────────────────────────────────────────────────────────

    private static void Report(TextBlock status, string message, IBrush brush)
    {
        status.Text = message;
        status.Foreground = brush;
        status.IsVisible = true;
    }

    private static (Control Root, TextBox Box) Field(string label, bool isPassword = false)
    {
        var box = new TextBox { Classes = { "search" } };
        if (isPassword)
        {
            box.PasswordChar = '*';
            // Nothing in this app should offer to remember or reveal it.
            box.Watermark = "";
        }

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = TypographySettings.Size(9),
            LetterSpacing = 1,
            Foreground = Palette.TextFaint
        });
        stack.Children.Add(box);
        return (stack, box);
    }

    private static Border OptionCard(string title, string detail, Action onPick)
    {
        var column = new StackPanel { Spacing = 5 };
        column.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = TypographySettings.Size(12),
            LetterSpacing = 1,
            Foreground = Palette.TextBright
        });
        column.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = TypographySettings.Size(10),
            LineHeight = TypographySettings.Size(15),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.TextFaint
        });

        var card = new Border
        {
            Padding = new Thickness(14, 12),
            Background = Palette.BgField,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = column
        };
        card.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            onPick();
        };
        return card;
    }

    private static void Select(Border picked, Border other)
    {
        picked.BorderBrush = Palette.Amber;
        other.BorderBrush = Palette.Border;
    }

    private static Control ContactFooter() => new TextBlock
    {
        Text = $"Questions? {AuthOptions.ContactEmail}",
        FontSize = TypographySettings.Size(10),
        Margin = new Thickness(0, 2, 0, 0),
        Foreground = Palette.TextFaint
    };
}
