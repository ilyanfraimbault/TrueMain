import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'
import ProcessTooltipContent from '~~/app/components/ProcessTooltipContent.vue'
import { CATALOG } from '../pipeline-chain/catalog'

// A step chip's tooltip: what the step does in the catalog's words, above its context line.
vi.mock('~/composables/usePipelineCatalog', () => ({ usePipelineCatalog: () => ref(CATALOG) }))

describe('ProcessTooltipContent', () => {
  it('describes a step the catalog lists, above the context', () => {
    const wrapper = mount(ProcessTooltipContent, { props: { processName: 'Harvest', context: 'Click to open the run' } })

    const lines = wrapper.findAll('p').map(line => line.text())
    expect(lines).toEqual([CATALOG.steps.find(step => step.name === 'Harvest')!.description, 'Click to open the run'])
  })

  it('shows only the context for a step the catalog does not list', () => {
    const wrapper = mount(ProcessTooltipContent, { props: { processName: 'RetiredStep', context: 'RetiredStep' } })

    expect(wrapper.findAll('p').map(line => line.text())).toEqual(['RetiredStep'])
  })
})
