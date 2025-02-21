using HtmlAgilityPack;
using System.Text;
using System.Collections.Concurrent;

namespace WebScrapingProcessTech
{
    class Program
    {
        static ConcurrentBag<string> urlsProcessadas = new ConcurrentBag<string>();
        static ConcurrentBag<(string Nome, string Descricao)> produtos = new ConcurrentBag<(string Nome, string Descricao)>();
        static string baseUrl = "https://www.processtec.com.br";
        static volatile bool isPaused = false;
        static volatile bool isRunning = true;
        static int totalPaginas = 0;
        static int paginasProcessadas = 0;
        static readonly SemaphoreSlim semaphore = new(5); // Limita a 5 requisições simultâneas

        static async Task Main()
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
                ExibirMenu();

                // Iniciar thread para monitorar comandos do usuário
                _ = Task.Run(MonitorarComandos);

                var htmlWeb = new HtmlWeb();
                htmlWeb.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36";

                while (isRunning)
                {
                    if (!isPaused)
                    {
                        await ProcessarPagina(baseUrl, htmlWeb);
                        isRunning = false; // Finaliza após processar tudo
                    }
                    else
                    {
                        await Task.Delay(1000); // Aguarda enquanto estiver pausado
                    }
                }

                await SalvarResultados();
                Console.WriteLine("\nProcessamento finalizado! Pressione qualquer tecla para sair.");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nErro geral: {ex.Message}");
            }
        }

        static void ExibirMenu()
        {
            Console.Clear();
            Console.WriteLine("=== Web Scraping Process Tech ===");
            Console.WriteLine("Comandos disponíveis:");
            Console.WriteLine("P - Pausar/Continuar");
            Console.WriteLine("S - Salvar resultados parciais");
            Console.WriteLine("Q - Sair");
            Console.WriteLine("===============================\n");
        }

        static async Task MonitorarComandos()
        {
            while (isRunning)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    switch (key)
                    {
                        case ConsoleKey.P:
                            isPaused = !isPaused;
                            Console.WriteLine($"\nProcessamento {(isPaused ? "PAUSADO" : "CONTINUANDO")}...");
                            break;
                        case ConsoleKey.S:
                            await SalvarResultados();
                            break;
                        case ConsoleKey.Q:
                            isRunning = false;
                            Console.WriteLine("\nFinalizando...");
                            break;
                    }
                }
                await Task.Delay(100);
            }
        }

        static async Task ProcessarPagina(string url, HtmlWeb htmlWeb)
        {
            if (urlsProcessadas.Contains(url))
                return;

            try
            {
                await semaphore.WaitAsync();
                try
                {
                    if (!urlsProcessadas.Contains(url))
                    {
                        totalPaginas++;
                        AtualizarProgresso();

                        var htmlDocument = await Task.Run(() => htmlWeb.Load(url));
                        if (htmlDocument == null) return;

                        urlsProcessadas.Add(url);
                        paginasProcessadas++;

                        var tasks = new List<Task>();

                        // Processar produtos
                        var produtoLinks = htmlDocument.DocumentNode.SelectNodes("//a[contains(@href, '/produto/')]");
                        if (produtoLinks != null)
                        {
                            foreach (var link in produtoLinks)
                            {
                                if (!isRunning) break;

                                var produtoUrl = link.GetAttributeValue("href", "");
                                if (!string.IsNullOrEmpty(produtoUrl))
                                {
                                    if (!produtoUrl.StartsWith("http"))
                                        produtoUrl = baseUrl + produtoUrl;

                                    tasks.Add(ProcessarProduto(produtoUrl, htmlWeb));
                                }
                            }
                        }

                        // Processar links de navegação
                        var paginationLinks = htmlDocument.DocumentNode.SelectNodes("//a[contains(@href, '/categoria/') or contains(@href, '/pagina/')]");
                        if (paginationLinks != null)
                        {
                            foreach (var link in paginationLinks)
                            {
                                if (!isRunning) break;

                                var nextUrl = link.GetAttributeValue("href", "");
                                if (!string.IsNullOrEmpty(nextUrl))
                                {
                                    if (!nextUrl.StartsWith("http"))
                                        nextUrl = baseUrl + nextUrl;

                                    if (!urlsProcessadas.Contains(nextUrl))
                                    {
                                        tasks.Add(ProcessarPagina(nextUrl, htmlWeb));
                                    }
                                }
                            }
                        }

                        await Task.WhenAll(tasks);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nErro ao processar página {url}: {ex.Message}");
            }
        }

        static async Task ProcessarProduto(string url, HtmlWeb htmlWeb)
        {
            if (urlsProcessadas.Contains(url))
                return;

            try
            {
                await semaphore.WaitAsync();
                try
                {
                    if (!urlsProcessadas.Contains(url))
                    {
                        urlsProcessadas.Add(url);
                        var htmlDocument = await Task.Run(() => htmlWeb.Load(url));

                        var nomeNode = htmlDocument.DocumentNode.SelectSingleNode("//h1 | //div[contains(@class, 'product-name')] | //div[contains(@class, 'title')]");
                        var descricaoNode = htmlDocument.DocumentNode.SelectSingleNode("//div[contains(@class, 'description')] | //div[contains(@class, 'details')] | //div[contains(@class, 'product-description')]");

                        if (nomeNode != null)
                        {
                            var nome = nomeNode.InnerText.Trim();
                            var descricao = descricaoNode?.InnerText.Trim() ?? "Sem descrição";

                            produtos.Add((nome, descricao));
                            AtualizarProgresso();
                        }
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nErro ao processar produto {url}: {ex.Message}");
            }
        }

        static void AtualizarProgresso()
        {
            Console.SetCursorPosition(0, 6);
            Console.WriteLine($"Páginas processadas: {paginasProcessadas}/{totalPaginas}");
            Console.WriteLine($"Produtos encontrados: {produtos.Count}          ");
            Console.WriteLine($"Status: {(isPaused ? "PAUSADO" : "EM EXECUÇÃO")}          ");
        }

        static async Task SalvarResultados()
        {
            try
            {
                string fileName = $"produtos_processtec_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                var produtosOrdenados = produtos.OrderBy(p => p.Nome).ToList();

                var json = System.Text.Json.JsonSerializer.Serialize(produtosOrdenados, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

                await File.WriteAllTextAsync(fileName, json, Encoding.UTF8);
                Console.WriteLine($"\nResultados salvos em: {fileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nErro ao salvar resultados: {ex.Message}");
            }
        }
    }
}
