import { RenderHost } from './RenderHost';
import { EntityStore } from './EntityStore';

export interface IComponent {
    unbind(): void;
}

export interface IComponentType {
    new (host: RenderHost, entityStore: EntityStore, element: HTMLElement): IComponent;
}