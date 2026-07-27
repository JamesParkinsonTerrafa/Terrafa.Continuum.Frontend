import { dotnet } from './_framework/dotnet.js'

const boot = document.getElementById('boot')

function bootFailed (message) {
  if (!boot) return
  boot.textContent = message
  boot.style.color = '#B4472F'
}

try {
  const runtime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create()

  const config = runtime.getConfig()

  // Avalonia paints its first frame during runMain, so the splash is torn down after it
  // resolves rather than on DOM ready — otherwise the plate flashes empty in between.
  runtime.runMain(config.mainAssemblyName, [globalThis.location.href])
    .catch(err => {
      console.error(err)
      bootFailed('Continuum core failed to start')
    })

  requestAnimationFrame(() => {
    if (!boot) return
    boot.classList.add('done')
    setTimeout(() => { boot.hidden = true }, 260)
  })
} catch (err) {
  console.error(err)
  bootFailed('Continuum core failed to load')
}
