using CasinoShiz.Services.Admin;
using CasinoShiz.Services.Poker.Application;
using CasinoShiz.Services.Poker.Presentation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace CasinoShiz.Pages.Admin;

public class PokerKickModel(AdminService admin, ITelegramBotClient bot, ILogger<PokerKickModel> logger) : PageModel
{
    public async Task<IActionResult> OnPostAsync(long id, CancellationToken ct)
    {
        var result = await admin.KickFromPokerAsync(callerId: -1, targetUserId: id, ct);
        if (result.RemainingSnapshot != null)
            await BroadcastStateAsync(result.RemainingSnapshot, ct);
        return Redirect($"/admin/user/{id}");
    }

    private async Task BroadcastStateAsync(TableSnapshot snap, CancellationToken ct)
    {
        foreach (var seat in snap.Seats.Where(s => s.ChatId != 0))
        {
            var text = PokerStateRenderer.RenderTable(snap.Table, snap.Seats, seat.UserId);
            try
            {
                if (seat.StateMessageId.HasValue)
                {
                    await bot.EditMessageText(seat.ChatId, seat.StateMessageId.Value, text,
                        parseMode: ParseMode.Html, cancellationToken: ct);
                }
                else
                {
                    await bot.SendMessage(seat.ChatId, text, parseMode: ParseMode.Html, cancellationToken: ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "admin.kick.broadcast_failed user={U}", seat.UserId);
            }
        }
    }
}
