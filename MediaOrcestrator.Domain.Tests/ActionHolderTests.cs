using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MediaOrcestrator.Domain.Tests;

[TestFixture]
public class ActionHolderTests
{
    private static ActionHolder CreateHolder()
    {
        return new(NullLogger<ActionHolder>.Instance);
    }

    [Test]
    public void Регистрация_создаёт_активное_действие_в_реестре()
    {
        var holder = CreateHolder();
        using var cts = new CancellationTokenSource();

        var act = holder.Register("test", "Старт", 10, cts);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(act.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(act.Name, Is.EqualTo("test"));
            Assert.That(act.Status, Is.EqualTo("Старт"));
            Assert.That(act.ProgressMax, Is.EqualTo(10));
            Assert.That(act.ProgressValue, Is.Zero);
            Assert.That(holder.Actions.ContainsKey(act.Id), Is.True);
        }
    }

    [Test]
    public void Завершение_не_отменяет_токен()
    {
        var holder = CreateHolder();
        var cts = new CancellationTokenSource();
        var act = holder.Register("test", "Старт", 0, cts);

        act.Finish();

        Assert.That(act.CancellationTokenSource.IsCancellationRequested, Is.False);
    }

    [Test]
    public void Завершение_удаляет_действие_из_реестра()
    {
        var holder = CreateHolder();
        var cts = new CancellationTokenSource();
        var act = holder.Register("test", "Старт", 0, cts);

        act.Finish();

        Assert.That(holder.Actions.ContainsKey(act.Id), Is.False);
    }

    [Test]
    public void Завершение_проставляет_статус_по_умолчанию_и_пользовательский()
    {
        var holder = CreateHolder();
        var cts1 = new CancellationTokenSource();
        var actDefault = holder.Register("a", "Старт", 0, cts1);

        actDefault.Finish();

        Assert.That(actDefault.Status, Is.EqualTo("Выполнено"));

        var cts2 = new CancellationTokenSource();
        var actCustom = holder.Register("b", "Старт", 0, cts2);

        actCustom.Finish("Готово");

        Assert.That(actCustom.Status, Is.EqualTo("Готово"));
    }

    [Test]
    public void Отмена_отменяет_токен_и_удаляет_действие()
    {
        var holder = CreateHolder();
        var cts = new CancellationTokenSource();
        var act = holder.Register("test", "Старт", 0, cts);

        act.Cancel();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cts.IsCancellationRequested, Is.True);
            Assert.That(act.Status, Is.EqualTo("Отменено"));
            Assert.That(holder.Actions.ContainsKey(act.Id), Is.False);
        }
    }

    [Test]
    public void Повторное_завершение_не_перезаписывает_статус()
    {
        var holder = CreateHolder();
        var cts = new CancellationTokenSource();
        var act = holder.Register("test", "Старт", 0, cts);

        act.Finish("X");
        act.Finish("Y");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(act.Status, Is.EqualTo("X"));
            Assert.That(holder.Actions.ContainsKey(act.Id), Is.False);
        }
    }

    [Test]
    public void Отмена_после_завершения_не_дёргает_токен()
    {
        var holder = CreateHolder();
        var cts = new CancellationTokenSource();
        var act = holder.Register("test", "Старт", 0, cts);

        act.Finish("Готово");
        act.Cancel();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cts.IsCancellationRequested, Is.False);
            Assert.That(act.Status, Is.EqualTo("Готово"));
        }
    }

    [Test]
    public void Завершение_после_отмены_сохраняет_статус_отмены()
    {
        var holder = CreateHolder();
        var cts = new CancellationTokenSource();
        var act = holder.Register("test", "Старт", 0, cts);

        act.Cancel();
        act.Finish();

        Assert.That(act.Status, Is.EqualTo("Отменено"));
    }

    [Test]
    public async Task Инкремент_прогресса_потокобезопасен_под_параллельной_нагрузкой()
    {
        var holder = CreateHolder();
        using var cts = new CancellationTokenSource();
        var act = holder.Register("test", "Старт", 100_000, cts);

        const int Tasks = 1000;
        const int Increments = 100;

        var pending = new Task[Tasks];
        for (var i = 0; i < Tasks; i++)
        {
            pending[i] = Task.Run(() =>
            {
                for (var j = 0; j < Increments; j++)
                {
                    act.ProgressPlus();
                }
            });
        }

        await Task.WhenAll(pending);

        Assert.That(act.ProgressValue, Is.EqualTo(Tasks * Increments));
    }

    [Test]
    public void Инкремент_через_холдер_увеличивает_прогресс()
    {
        var holder = CreateHolder();
        using var cts = new CancellationTokenSource();
        var act = holder.Register("test", "Старт", 10, cts);

        holder.ProgressPlus(act.Id);
        holder.ProgressPlus(act.Id);
        holder.ProgressPlus(act.Id);

        Assert.That(act.ProgressValue, Is.EqualTo(3));
    }

    [Test]
    public void Установка_статуса_через_холдер_эквивалентна_установке_через_свойство()
    {
        var holder = CreateHolder();
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();

        var actA = holder.Register("a", "Старт", 0, cts1);
        var actB = holder.Register("b", "Старт", 0, cts2);

        actA.Status = "Новый";
        holder.SetStatus(actB.Id, "Новый");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(actB.Status, Is.EqualTo(actA.Status));
            Assert.That(actA.Status, Is.EqualTo("Новый"));
        }
    }
}
