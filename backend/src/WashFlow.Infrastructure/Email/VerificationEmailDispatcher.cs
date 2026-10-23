using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WashFlow.Application.Authentication;

namespace WashFlow.Infrastructure.Email;

public sealed class VerificationEmailDispatcher(IEmailSender sender, TimeProvider clock,
    ILogger<VerificationEmailDispatcher> logger) : BackgroundService, IVerificationEmailQueue
{
    private readonly Channel<VerificationEmail> queue = Channel.CreateBounded<VerificationEmail>(
        new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    public bool TryEnqueue(VerificationEmail email)
    {
        if (queue.Writer.TryWrite(email)) return true;
        logger.LogWarning("Doğrulama e-posta kuyruğu dolu; yeniden gönderim gereklidir.");
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var email in queue.Reader.ReadAllAsync(stoppingToken))
        {
            for (var attempt = 0; attempt < 3 && email.ExpiresAt > clock.GetUtcNow(); attempt++)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    await sender.SendAsync(email, timeout.Token);
                    break;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception)
                {
                    // Never attach the provider exception, recipient, body or token to a log event.
                    logger.LogWarning("Doğrulama e-postası gönderilemedi.");
                    if (attempt < 2) await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
        }
    }
}
