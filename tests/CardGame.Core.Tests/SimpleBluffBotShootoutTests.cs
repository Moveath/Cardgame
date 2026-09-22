using CardGame.Bots;
using CardGame.Core.Cards;
using CardGame.Core.Games.Bluff;
using Xunit;

namespace CardGame.Core.Tests;

/// <summary>
/// Как бот ведёт себя в перестрелке. Механически проверка в ней ничем не отличается от
/// обычной — вся цена в петухе, — поэтому поведение здесь целиком про нервы: молчать,
/// пока котёл мал, и беречь настоящую карту круга на тот случай, когда всё-таки вскроют.
/// </summary>
public class SimpleBluffBotShootoutTests
{
    private static readonly string[] Two = { "Егор", "Бот" };
    private static readonly string[] Three = { "Егор", "Бот", "Данил" };

    private static BluffOptions Shootouts => new(3, true, false, true);

    private const int Trials = 400;

    private static Deck BuildDeck(params Card[][] hands)
    {
        var longest = 0;
        foreach (var hand in hands)
        {
            if (hand.Length > longest)
            {
                longest = hand.Length;
            }
        }

        var cards = new List<Card>();
        for (var round = 0; round < longest; round++)
        {
            foreach (var hand in hands)
            {
                if (round < hand.Length)
                {
                    cards.Add(hand[round]);
                }
            }
        }

        return new Deck(cards);
    }

    /// <summary>Разные карты любых рангов, кроме названного и тузов — чистый мусор для рук.</summary>
    private static List<Card> JunkPool(Rank exclude)
    {
        var suits = new[] { Suit.Spades, Suit.Hearts, Suit.Diamonds, Suit.Clubs };
        var pool = new List<Card>();

        for (var rank = Rank.Two; rank <= Rank.King; rank++)
        {
            if (rank == exclude)
            {
                continue;
            }

            foreach (var suit in suits)
            {
                pool.Add(new Card(rank, suit));
            }
        }

        return pool;
    }

    /// <summary>
    /// Перестрелку предлагает сама раздача, а раздачу здесь мы задаём руками. Чтобы всё-таки
    /// получить нужную позицию именно в перестрелке, крутим порядок карт внутри рук: состав
    /// от этого не меняется — кому что достанется, решает номер руки, а не порядок в ней, —
    /// зато меняется зерно, по которому решается предложение.
    /// </summary>
    private static BluffGame ShootoutOffered(IReadOnlyList<string> names, params Card[][] hands)
    {
        for (var shift = 1; shift <= 96; shift++)
        {
            var rotated = new Card[hands.Length][];
            for (var i = 0; i < hands.Length; i++)
            {
                var hand = hands[i];
                var by = shift * (i + 1) % hand.Length;

                rotated[i] = new Card[hand.Length];
                for (var k = 0; k < hand.Length; k++)
                {
                    rotated[i][k] = hand[(k + by) % hand.Length];
                }
            }

            var game = BluffGame.Create(names, BuildDeck(rotated), Shootouts);
            if (game.ShootoutOffered)
            {
                return game;
            }
        }

        Assert.Fail("Ни на одной перестановке рук перестрелку не предложили.");
        return null!;
    }

    private static void Apply(BluffGame game, int player, BluffMove move)
    {
        Assert.True(game.TryApplyMove(player, move, out var error), error);
    }

    /// <summary>Сколько раз из <see cref="Trials"/> бот решил вскрыть последний пакет.</summary>
    private static int ChallengeRate(BluffGame game, int botIndex)
    {
        var count = 0;
        for (var seed = 0; seed < Trials; seed++)
        {
            if (new SimpleBluffBot(new Random(seed)).ShouldChallenge(game, botIndex))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Доказанная ложь: бот держит две шестёрки, а сосед заявляет ещё три.</summary>
    private static Card[] ThreeJunk(List<Card> pool, int from) =>
        new[] { pool[from], pool[from + 1], pool[from + 2] };

    // ---------- молчание ----------

    /// <summary>
    /// Та же позиция, та же доказанная ложь, та же рука — разница только в том, что идёт
    /// перестрелка. Вне её бот вскрывает такое почти всегда, внутри — почти никогда.
    /// </summary>
    [Fact]
    public void Bot_SwallowsAProvenLieWhileTheShootoutIsYoung()
    {
        var pool = JunkPool(Rank.Six);
        var liar = new List<Card>(pool.GetRange(0, 12));
        var bot = new List<Card> { new(Rank.Six, Suit.Spades), new(Rank.Six, Suit.Hearts) };
        bot.AddRange(pool.GetRange(12, 10));

        var loud = ShootoutOffered(Two, liar.ToArray(), bot.ToArray());
        var plain = BluffGame.Create(Two, BuildDeck(liar.ToArray(), bot.ToArray()), BluffOptions.Classic);

        var thrown = ThreeJunk(liar, 0);
        Apply(loud, 0, BluffMove.DeclareShootout(Rank.Six, thrown));
        Apply(plain, 0, BluffMove.Declare(Rank.Six, thrown));

        var inShootout = ChallengeRate(loud, 1);
        var normally = ChallengeRate(plain, 1);

        Assert.True(normally > 200, "вне перестрелки вскрывает всего " + normally);
        Assert.True(inShootout * 4 < normally,
            "в перестрелке " + inShootout + ", обычно " + normally);
    }

    /// <summary>
    /// Но не до бесконечности. Гора карт в котле достанется пойманному — и чем она выше,
    /// тем громче соблазн. Позиция та же, рука та же, разница только в размере котла.
    /// </summary>
    [Fact]
    public void Bot_StartsToCrackWhenThePotGetsHuge()
    {
        var pool = JunkPool(Rank.Six);
        var liar = new List<Card>(pool.GetRange(0, 18));
        var bot = new List<Card> { new(Rank.Six, Suit.Spades), new(Rank.Six, Suit.Hearts) };
        bot.AddRange(pool.GetRange(18, 16));

        var game = ShootoutOffered(Two, liar.ToArray(), bot.ToArray());

        Apply(game, 0, BluffMove.DeclareShootout(Rank.Six, ThreeJunk(liar, 0)));
        var young = ChallengeRate(game, 1);

        // Гоняем круг дальше, пока котёл не вырастет. Руки тают поровну, так что лидера
        // за столом не появляется: проверяем именно влияние котла.
        for (var lap = 1; lap <= 3; lap++)
        {
            Apply(game, 1, BluffMove.Follow(ThreeJunk(bot, 2 + (lap - 1) * 3)));
            Apply(game, 0, BluffMove.Follow(ThreeJunk(liar, lap * 3)));
        }

        Assert.True(game.DeclaredPotCount >= 20, "котёл всего " + game.DeclaredPotCount);

        var huge = ChallengeRate(game, 1);

        Assert.True(huge > young * 3 / 2, "на маленьком котле " + young + ", на большом " + huge);
    }

    // ---------- запас ----------

    /// <summary>
    /// Главный приём перестрелки: настоящая карта круга — это страховка. Пока котёл мал и
    /// вскрывать никто не станет, бот валит мусор, а шестёрку придерживает. Когда котёл
    /// вырос и проверка вот-вот случится, страховку пускают в ход.
    /// </summary>
    [Fact]
    public void Bot_HoldsItsOnlyRealCardBackUntilThePotGetsDangerous()
    {
        var pool = JunkPool(Rank.Six);
        var liar = new List<Card>(pool.GetRange(0, 18));
        var bot = new List<Card> { new(Rank.Six, Suit.Spades) };
        bot.AddRange(pool.GetRange(18, 17));

        var game = ShootoutOffered(Two, liar.ToArray(), bot.ToArray());
        Apply(game, 0, BluffMove.DeclareShootout(Rank.Six, ThreeJunk(liar, 0)));

        var keptEarly = KeptBackRate(game);

        for (var lap = 1; lap <= 3; lap++)
        {
            Apply(game, 1, BluffMove.Follow(ThreeJunk(bot, 1 + (lap - 1) * 3)));
            Apply(game, 0, BluffMove.Follow(ThreeJunk(liar, lap * 3)));
        }

        var keptLate = KeptBackRate(game);

        Assert.True(keptEarly > keptLate, "в начале придержал " + keptEarly + ", потом " + keptLate);
        Assert.True(keptLate > 0, "к большому котлу перестал придерживать вовсе");
    }

    /// <summary>Сколько раз из <see cref="Trials"/> бот соврал, не тронув свою настоящую карту круга.</summary>
    private static int KeptBackRate(BluffGame game)
    {
        var kept = 0;

        for (var seed = 0; seed < Trials; seed++)
        {
            var move = new SimpleBluffBot(new Random(seed)).ChooseMove(game, 1);
            if (move == null || move.Type != BluffMoveType.Follow)
            {
                continue;
            }

            var honest = false;
            foreach (var card in move.Cards)
            {
                if (card.Rank == Rank.Six)
                {
                    honest = true;
                }
            }

            if (!honest)
            {
                kept++;
            }
        }

        return kept;
    }

    // ---------- объявлять или нет ----------

    /// <summary>
    /// Перестрелка — лотерея с огромным котлом. Тому, кто вот-вот выйдет, она невыгодна;
    /// тому, кто тонет в картах, терять уже нечего. Считаем не отдельный случай, а средний
    /// расклад на сотнях партий: у тех, кто согласился, карт на руках больше.
    /// </summary>
    [Fact]
    public void Bot_OpensShootoutMoreReadilyWhenItIsDrowningInCards()
    {
        var openedShare = 0.0;
        var opened = 0;
        var declinedShare = 0.0;
        var declined = 0;

        for (var seed = 0; seed < 400; seed++)
        {
            var game = BluffGame.Create(Three, Deck.Standard36(seed), Shootouts);
            var bot = new SimpleBluffBot(new Random(seed * 7 + 1));

            var guard = 0;
            while (!game.IsFinished && !game.ShootoutOffered && guard++ < 200)
            {
                var actor = game.CurrentPlayerIndex;
                var move = bot.ChooseMove(game, actor);
                if (move == null)
                {
                    break;
                }

                Apply(game, actor, move);
            }

            if (game.IsFinished || !game.ShootoutOffered)
            {
                continue;
            }

            var leader = game.CurrentPlayerIndex;
            var share = HandShare(game, leader);

            var decision = bot.ChooseMove(game, leader);
            Assert.NotNull(decision);

            if (decision!.OpensShootout)
            {
                opened++;
                openedShare += share;
            }
            else
            {
                declined++;
                declinedShare += share;
            }
        }

        Assert.True(opened > 30, "согласился всего " + opened + " раз");
        Assert.True(declined > 30, "отказался всего " + declined + " раз");
        Assert.True(openedShare / opened > declinedShare / declined,
            "согласившиеся держат " + openedShare / opened + " от среднего, отказавшиеся " + declinedShare / declined);
    }

    /// <summary>Сколько карт у игрока относительно среднего по столу.</summary>
    private static double HandShare(BluffGame game, int playerIndex)
    {
        var total = 0;
        var holders = 0;

        foreach (var player in game.Players)
        {
            if (player.IsOut)
            {
                continue;
            }

            total += player.Hand.Count;
            holders++;
        }

        return total == 0 ? 0.0 : game.Players[playerIndex].Hand.Count / (total / (double)holders);
    }

    // ---------- партия целиком ----------

    /// <summary>
    /// Боты с перестрелками доигрывают партию сами и не зависают: круг в перестрелке
    /// длинный, но кто-нибудь всё равно однажды не выдерживает.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    public void BotsFinishGamesWithShootouts(int playerCount)
    {
        var names = new List<string>();
        for (var i = 0; i < playerCount; i++)
        {
            names.Add("Бот" + i);
        }

        var shootouts = 0;
        var roosters = 0;

        for (var seed = 0; seed < 25; seed++)
        {
            var game = BluffGame.Create(names, Deck.Standard36(seed), Shootouts);
            var bots = new List<SimpleBluffBot>();
            for (var i = 0; i < playerCount; i++)
            {
                bots.Add(new SimpleBluffBot(new Random(seed * 100 + i)));
            }

            var guard = 0;
            while (!game.IsFinished)
            {
                Assert.True(guard++ < 600, "партия не сходится: seed " + seed);

                var actor = game.CurrentPlayerIndex;
                var wasOffered = game.ShootoutOffered;

                var move = bots[actor].ChooseFourOfAKind(game, actor) ?? bots[actor].ChooseMove(game, actor);
                Assert.NotNull(move);
                Apply(game, actor, move!);

                if (wasOffered && move!.OpensShootout)
                {
                    shootouts++;
                }
            }

            foreach (var player in game.Players)
            {
                roosters += player.RoosterMarks;
            }
        }

        Assert.True(shootouts > 5, "перестрелок за 25 партий всего " + shootouts);
        Assert.True(roosters > 0, "петухов не завелось ни одного — перестрелки никто не срывает");
        Assert.True(roosters < shootouts, "перестрелки срывают чаще, чем объявляют");
    }
}
